using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 怪物状态枚举：定义怪物三种行为状态
/// Patrol 巡逻 | Chase 追击 | Attack 攻击
/// </summary>
public enum MonsterState
{
    Patrol,
    Chase,
    Attack
}

/// <summary>
/// 怪物有限状态机AI
/// 修复问题：追击计数标记卡死、状态切换忘记取消计分
/// 优化：寻路不再每帧调用、视线检测降频、速度与阈值可在 Inspector 调整
/// </summary>
public class MonsterFSM : MonoBehaviour
{
    [Header("基础设置")]
    public MonsterState currentState;
    public Transform player;        // 玩家对象拖拽赋值

    [Header("距离参数")]
    public float detectDistance = 16f;  // 侦测玩家最大距离
    public float attackDistance = 2f;   // 进入攻击范围距离

    [Header("移动速度")]
    public float patrolSpeed = 3.5f;    // 巡逻速度（原来写死在代码里的 3.5f）
    public float chaseSpeed = 7.5f;     // 追击速度（原来写死在代码里的 7.5f）

    [Header("巡逻配置")]
    public Transform[] patrolPoints;    // 多个巡逻点位数组
    public float patrolWaitTime = 2f;   // 到达巡逻点等待时间
    public float arriveThreshold = 0.5f;// 判定"到达"的距离阈值（按水平距离算，忽略高度差）
    public float patrolStallTimeout = 8f;// 到不了当前巡逻点超过这么久就跳过，避免永久卡死
    public float navMeshSampleRadius = 2f;// 把巡逻点吸附到导航网格的搜索半径，防止点位掉出网格

    [Header("追击配置")]
    public float losePlayerDelay = 4f;  // 丢失玩家后等待多久返回巡逻

    [Header("视野设置")]
    public LayerMask obstacleLayer;     // 障碍物层级，射线碰到代表被挡住视线
    public float visionCheckInterval = 0.1f;  // 视线检测间隔（秒），不必每帧打射线
    public float chaseRepathInterval = 0.1f;  // 追击时重新寻路的间隔（秒）

    private NavMeshAgent agent;
    private bool isChasing = false;     // 标记：当前怪物是否正在追击玩家
    private int currentPatrolIndex = 0; // 当前前往第几个巡逻点
    private float waitTimer;            // 巡逻等待计时器
    private float stallTimer;           // 巡逻卡住计时（到不了当前点）
    private float losePlayerTimer;      // 丢失玩家倒计时

    private float visionTimer;          // 视线检测计时
    private bool canSeePlayer;          // 视线检测缓存结果
    private float repathTimer;          // 追击寻路计时
    private bool hasDestination;        // 是否已经给 agent 下过目标
    private Vector3 lastDestination;    // 上次下达的目标点
    private Vector3 spawnPosition;      // 出生点，重启复位用

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        if (agent == null)
        {
            Debug.LogError($"{name}：缺少 NavMeshAgent 组件，怪物AI已关闭", this);
            enabled = false;
            return;
        }

        // 放在 Start 里取：此时 agent 已经吸附到 NavMesh 上，Warp 回得去
        spawnPosition = transform.position;

        currentState = MonsterState.Patrol;
        isChasing = false;
        losePlayerTimer = 0f;
        waitTimer = 0f;
        repathTimer = 0f;
        visionTimer = 0f;
        hasDestination = false;
        agent.speed = patrolSpeed;
        CheckVisionNow();
    }

    void Update()
    {
        if (player == null) return;

        // 如果全局游戏结束，强制清除追击标记，直接退出逻辑
        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            ClearChaseMark();
            return;
        }

        // 视线检测降频：没必要每帧打一条射线
        visionTimer -= Time.deltaTime;
        if (visionTimer <= 0f)
        {
            CheckVisionNow();
            visionTimer = visionCheckInterval;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        bool seePlayer = distanceToPlayer <= detectDistance && canSeePlayer;

        // 兜底保护：玩家超出侦测范围，防止标记卡死
        if (distanceToPlayer > detectDistance && isChasing)
        {
            losePlayerTimer += Time.deltaTime;
            if (losePlayerTimer >= losePlayerDelay)
            {
                ReturnToPatrol();
            }
            return;
        }

        // 状态机主逻辑
        switch (currentState)
        {
            case MonsterState.Patrol:
                PatrolLogic();
                // 巡逻 → 切换追击
                if (seePlayer)
                {
                    EnterChase();
                }
                break;

            case MonsterState.Chase:
                ChaseLogic(seePlayer);
                // 追击距离足够近，切换攻击状态，停止计分
                if (distanceToPlayer < attackDistance)
                {
                    EnterAttack();
                }
                break;

            case MonsterState.Attack:
                AttackLogic(distanceToPlayer);
                break;
        }
    }

    /// <summary>
    /// 统一方法：清除追击标记，减少重复代码
    /// 所有怪物退出追击状态时调用
    /// </summary>
    void ClearChaseMark()
    {
        if (isChasing)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RemoveChaseEnemy();
            }
            isChasing = false;
        }
    }

    /// <summary>
    /// 射线视野检测：判断中间是否有墙体遮挡
    /// 结果缓存到 canSeePlayer，由 Update 按间隔调用，不再每帧执行
    /// </summary>
    void CheckVisionNow()
    {
        canSeePlayer = false;
        if (player == null) return;

        Vector3 origin = transform.position + Vector3.up;
        Vector3 toPlayer = player.position - origin;
        toPlayer.y = 0f;                    // 忽略高度差，只做水平检测

        float flatDistance = toPlayer.magnitude;
        if (flatDistance < 0.001f)          // 和玩家重合，直接算看得见
        {
            canSeePlayer = true;
            return;
        }
        if (flatDistance > detectDistance)  // 超出侦测范围，射线都不用打
            return;

        // 射线碰到障碍物 → 看不见玩家
        // Ignore 触发体：怪物和玩家的碰撞体都是 trigger，不该挡住视线
        canSeePlayer = !Physics.Raycast(origin, toPlayer / flatDistance, flatDistance,
                                        obstacleLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 巡逻行为逻辑
    /// </summary>
    void PatrolLogic()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return;

        Transform target = patrolPoints[currentPatrolIndex];
        if (target == null)
        {
            AdvancePatrolIndex();
            return;
        }

        // 巡逻点可能因为重新烘焙导航网格而落进墙周围的"空洞"里，先吸附到最近的可走位置
        Vector3 destination = ResolveOnNavMesh(target.position);

        // 目标没变就不要再调 SetDestination，原来每帧调用等于每帧重算一次路径
        if (!hasDestination || (destination - lastDestination).sqrMagnitude > 0.04f)
        {
            agent.SetDestination(destination);
            lastDestination = destination;
            hasDestination = true;
            waitTimer = 0f;
            stallTimer = 0f;
        }

        if (HasArrived(destination))
        {
            // 到达巡逻点位，开始等待
            waitTimer += Time.deltaTime;
            stallTimer = 0f;
            if (waitTimer >= patrolWaitTime)
            {
                waitTimer = 0f;
                AdvancePatrolIndex();
            }
        }
        else
        {
            waitTimer = 0f;

            // 兜底：巡逻点被墙挡住或根本走不到时，不能永久卡在这里不动
            stallTimer += Time.deltaTime;
            if (stallTimer >= patrolStallTimeout)
            {
                Debug.LogWarning($"{name}: 第 {currentPatrolIndex} 个巡逻点在 {patrolStallTimeout} 秒内无法到达，跳过", this);
                stallTimer = 0f;
                AdvancePatrolIndex();
            }
        }
    }

    /// <summary>
    /// 把目标点吸附到导航网格上最近的可走位置。
    /// 重新烘焙导航网格后，靠墙的巡逻点可能掉进"墙周围的空洞"里，
    /// 此时 SetDestination 会失败、怪物原地不动——吸附一下就能继续巡逻。
    /// </summary>
    Vector3 ResolveOnNavMesh(Vector3 raw)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(raw, out hit, navMeshSampleRadius, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return raw;
    }

    /// <summary>
    /// 是否已到达目标点
    /// 用水平距离 + NavMesh 剩余距离双重判定：
    /// 巡逻点与怪物落脚高度不一致时，只用 Vector3.Distance 会因 Y 轴落差永远判定不到达，
    /// 怪物就会卡在第一个巡逻点上再也不动
    /// </summary>
    bool HasArrived(Vector3 destination)
    {
        Vector3 delta = transform.position - destination;
        delta.y = 0f;
        if (delta.sqrMagnitude <= arriveThreshold * arriveThreshold)
            return true;

        return !agent.pathPending && agent.remainingDistance <= arriveThreshold;
    }

    /// <summary>
    /// 切到下一个巡逻点
    /// </summary>
    void AdvancePatrolIndex()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        hasDestination = false;   // 强制下一帧重新下达目标
    }

    /// <summary>
    /// 追击行为逻辑
    /// </summary>
    void ChaseLogic(bool seePlayer)
    {
        if (seePlayer)
        {
            losePlayerTimer = 0f;

            // 玩家一直在动，但也没必要每帧重算路径，按间隔刷新即可
            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                agent.SetDestination(player.position);
                lastDestination = player.position;
                hasDestination = true;
                repathTimer = chaseRepathInterval;
            }
        }
        else
        {
            losePlayerTimer += Time.deltaTime;
            // 超时没看见玩家，返回巡逻
            if (losePlayerTimer >= losePlayerDelay)
            {
                ReturnToPatrol();
            }
        }
    }

    /// <summary>
    /// 攻击行为逻辑
    /// </summary>
    void AttackLogic(float dis)
    {
        // 玩家拉开距离，脱离攻击范围，重新进入追击
        if (dis > attackDistance * 1.2f)
        {
            EnterChase();
            return;
        }

        // 持续贴近玩家 → 被抓到，游戏结束
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GameOver();
        }
    }

    /// <summary>
    /// 进入追击状态
    /// </summary>
    void EnterChase()
    {
        currentState = MonsterState.Chase;
        agent.speed = chaseSpeed;
        losePlayerTimer = 0f;
        repathTimer = 0f;       // 立刻下达一次目标
        hasDestination = false;

        if (!isChasing)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddChaseEnemy();
            }
            isChasing = true;
        }
    }

    /// <summary>
    /// 进入攻击状态
    /// </summary>
    void EnterAttack()
    {
        currentState = MonsterState.Attack;
        ClearChaseMark();
    }

    /// <summary>
    /// 返回巡逻状态
    /// </summary>
    void ReturnToPatrol()
    {
        currentState = MonsterState.Patrol;
        agent.speed = patrolSpeed;
        losePlayerTimer = 0f;
        hasDestination = false;
        ClearChaseMark();
    }

    /// <summary>
    /// 重启复位：由 GameManager.RestartGame 调用
    /// 清空状态机并把怪物搬回出生点，避免重启瞬间还贴在玩家身上
    /// </summary>
    public void ResetMonster()
    {
        currentState = MonsterState.Patrol;
        currentPatrolIndex = 0;
        waitTimer = 0f;
        stallTimer = 0f;
        losePlayerTimer = 0f;
        repathTimer = 0f;
        visionTimer = 0f;
        hasDestination = false;
        // 这里直接清标记、不回调 GameManager：
        // 重启时 GameManager 已经把追击计数清零了，再回调反而会减出负数
        isChasing = false;

        if (agent == null) return;

        agent.speed = patrolSpeed;
        if (!agent.enabled) agent.enabled = true;
        agent.ResetPath();

        // 搬回出生点（Warp 要求 agent 在 NavMesh 上）
        if (agent.isOnNavMesh)
        {
            agent.Warp(spawnPosition);
        }

        CheckVisionNow();
    }
}
