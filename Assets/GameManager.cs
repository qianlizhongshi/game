using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;   // 场景里的文本是 TextMeshProUGUI，不是旧版 UnityEngine.UI.Text

/// <summary>
/// 游戏全局管理器：统一管理倒计时、计分、游戏结束状态、本局排行榜
/// 单例模式，怪物脚本可以 GameManager.Instance.xxx 调用功能
/// </summary>
public class GameManager : MonoBehaviour
{
    // 单例静态实例，全局访问入口
    public static GameManager Instance;

    [Header("UI绑定区域")]
    public TMP_Text timeText;       // 倒计时文本（TextMeshProUGUI）
    public TMP_Text scoreText;      // 分数文本（TextMeshProUGUI）
    public Button restartButton;    // 游戏结束后显示的重启按钮

    [Header("结算界面")]
    public GameObject gameOverTitle;    // Canvas 下的 "GAME OVER"
    public TMP_Text finalScoreText;     // Canvas 下的 "FinalScoreText"：分数 + 名次 + 排行榜

    [Header("游戏基础配置")]
    public float totalGameTime = 120f;  // 总游戏时长：120秒 = 2分钟
    private float remainTime;           // 剩余倒计时时间

    public float currentScore;          // 当前总分
    public bool isGameOver = false;     // 游戏结束锁，结束后停止所有逻辑

    [Header("重启复位（留空会自动在场景里查找）")]
    public MonsterFSM[] monsters;       // 重启时全部复位回巡逻
    public PlayerController player;     // 重启时放回出生点

    [Header("排行榜")]
    public int leaderboardEntriesToShow = 5;      // 结算界面列出前几名
    // 名次文案。前三名想显示"冠军/亚军/季军"的话，等字体支持中文后在这里改；
    // 留空的名次会自动退回 "4." "5." 这种纯序号形式。
    public string[] rankLabels = { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th" };
    public string currentRunMark = "NEW";         // 标记哪一条是本局刚打出来的
    public string leaderboardHeader = "RANKING";

    [Header("文案（当前字体只有拉丁字形，中文会显示成空白）")]
    public string timeLabel = "TIME";
    public string scoreLabel = "SCORE";
    public string finalScoreLabel = "SCORE";
    public string rankLabel = "RANK";

    private int chasingEnemyCount = 0;  // 正在追击玩家的怪物数量（用来实现双倍加分）
    private int lastRank = 0;           // 本局名次，0 表示没进榜

    // UI 只在显示内容真的变化时才写 text：
    // 原来每帧拼一次插值字符串并刷新 Canvas，等于每帧一次 GC 分配
    private float lastShownTime = float.NaN;
    private int lastShownScore = int.MinValue;
    private const float TimeDisplayStep = 0.05f;   // 显示精度 0.1 秒，取一半作为变化阈值

    void Awake()
    {
        // 单例初始化，保证场景只有一个GameManager
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void OnDestroy()
    {
        // 场景卸载后清掉静态引用，避免留下悬空引用
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Start()
    {
        // 没在 Inspector 里拖的，自动在场景里找一遍，避免忘记接线就整个失效
        if (monsters == null || monsters.Length == 0)
        {
            monsters = FindObjectsOfType<MonsterFSM>();
        }
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }

        ResetGameState();

        // 绑定按钮点击事件：点击重启游戏
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartGame);
        }

        // 开局先收起结算界面和重启按钮
        if (gameOverTitle != null) gameOverTitle.SetActive(false);
        if (finalScoreText != null) finalScoreText.gameObject.SetActive(false);
        if (restartButton != null) restartButton.gameObject.SetActive(false);

        RefreshUI(true);
    }

    void Update()
    {
        // 游戏结束，不再执行任何逻辑
        if (isGameOver)
            return;

        // 倒计时持续减少
        remainTime -= Time.deltaTime;

        // 持续加分：追击怪物数量 × 时间，1只每秒+1分，2只每秒+2分
        currentScore += chasingEnemyCount * Time.deltaTime;

        // 倒计时耗尽，触发游戏结束
        if (remainTime <= 0f)
        {
            remainTime = 0f;
            GameOver();
        }

        RefreshUI();
    }

    /// <summary>
    /// 把一局的运行时数据恢复到初始状态（不含怪物、玩家和 UI）
    /// 注意：排行榜不在这里清，它要跨多局累积，只在程序重启时才没了
    /// </summary>
    void ResetGameState()
    {
        remainTime = totalGameTime;
        currentScore = 0f;
        isGameOver = false;
        chasingEnemyCount = 0;
        lastRank = 0;
    }

    /// <summary>
    /// 怪物进入追击状态，登记追击数量（MonsterFSM调用）
    /// </summary>
    public void AddChaseEnemy()
    {
        if (isGameOver) return;
        chasingEnemyCount++;
    }

    /// <summary>
    /// 怪物退出追击状态，减少追击数量（MonsterFSM调用）
    /// </summary>
    public void RemoveChaseEnemy()
    {
        if (isGameOver) return;
        // 最小值限制为0，防止负数
        chasingEnemyCount = Mathf.Max(chasingEnemyCount - 1, 0);
    }

    /// <summary>
    /// 游戏结束统一入口
    /// 触发条件：被怪物抓到 / 倒计时归零
    /// </summary>
    public void GameOver()
    {
        // 同一帧多个怪物同时抓到玩家时只结算一次
        if (isGameOver) return;
        isGameOver = true;

        int finalScore = Mathf.RoundToInt(currentScore);

        // 把这一局记进本局排行榜，拿到名次
        lastRank = Leaderboard.Submit(finalScore);

        Debug.Log($"游戏结束！最终分数：{finalScore}，本局名次：{(lastRank > 0 ? lastRank + " / " + Leaderboard.Count : "未进榜")}");

        // 弹出结算界面：GAME OVER + 分数/名次/排行榜
        if (gameOverTitle != null)
        {
            gameOverTitle.SetActive(true);
        }

        if (finalScoreText != null)
        {
            finalScoreText.text = BuildResultText(finalScore);
            finalScoreText.gameObject.SetActive(true);
        }

        // 显示重启按钮
        if (restartButton != null)
        {
            restartButton.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 拼结算界面文字：本局分数 + 本局名次 + 排行榜前几名
    /// </summary>
    string BuildResultText(int finalScore)
    {
        StringBuilder sb = new StringBuilder(320);

        sb.Append(finalScoreLabel).Append(' ').Append(finalScore);
        sb.Append('\n').Append(rankLabel).Append(' ');
        if (lastRank > 0)
        {
            sb.Append(lastRank).Append(" / ").Append(Leaderboard.Count);
        }
        else
        {
            sb.Append('-');
        }

        sb.Append("\n\n").Append(leaderboardHeader);

        int shown = Mathf.Clamp(leaderboardEntriesToShow, 0, Leaderboard.Count);
        for (int rank = 1; rank <= shown; rank++)
        {
            sb.Append('\n').Append(GetRankLabel(rank)).Append("  ").Append(Leaderboard.GetScore(rank));
            if (rank == lastRank)
            {
                sb.Append(' ').Append(currentRunMark);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 第 rank 名的文案；没配置就退回 "4." "5." 这种纯序号
    /// </summary>
    string GetRankLabel(int rank)
    {
        if (rankLabels != null && rank >= 1 && rank <= rankLabels.Length &&
            !string.IsNullOrEmpty(rankLabels[rank - 1]))
        {
            return rankLabels[rank - 1];
        }
        return rank + ".";
    }

    /// <summary>
    /// 刷新界面文字
    /// 只有显示内容真的变了才写 text，避免每帧刷 Canvas
    /// </summary>
    /// <param name="force">true 时强制刷新一次（初始化、重启）</param>
    void RefreshUI(bool force = false)
    {
        if (timeText != null)
        {
            float shown = Mathf.Max(remainTime, 0f);
            if (force || float.IsNaN(lastShownTime) || Mathf.Abs(shown - lastShownTime) >= TimeDisplayStep)
            {
                lastShownTime = shown;
                timeText.text = timeLabel + " " + shown.ToString("F1");
            }
        }

        if (scoreText != null)
        {
            int score = Mathf.RoundToInt(currentScore);
            if (force || score != lastShownScore)
            {
                lastShownScore = score;
                scoreText.text = scoreLabel + " " + score;
            }
        }
    }

    /// <summary>
    /// 重启游戏，重置所有数据
    /// 不只是时间和分数：怪物和玩家也要复位。排行榜保留，继续累积。
    /// </summary>
    public void RestartGame()
    {
        // 先复位一局的数据，再复位怪物和玩家。
        // 原来的实现没有复位怪物，被抓到时点重启，怪物还贴在玩家身上，
        // 下一帧攻击判定立刻再次触发 GameOver，等于重启无效。
        ResetGameState();

        if (monsters != null)
        {
            for (int i = 0; i < monsters.Length; i++)
            {
                if (monsters[i] != null)
                {
                    monsters[i].ResetMonster();
                }
            }
        }

        if (player != null)
        {
            player.ResetToSpawn();
        }

        // 收起结算界面和重启按钮
        if (gameOverTitle != null) gameOverTitle.SetActive(false);
        if (finalScoreText != null) finalScoreText.gameObject.SetActive(false);
        if (restartButton != null) restartButton.gameObject.SetActive(false);

        RefreshUI(true);
    }
}
