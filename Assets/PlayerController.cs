using UnityEngine;

/// <summary>
/// 玩家WASD移动控制
/// 文件名：PlayerController.cs
/// 增加重力，防止角色下坠；游戏结束禁止移动
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 3f;
    [Header("重力设置")]
    public float gravity = -9.81f;

    private CharacterController controller;
    private GameManager gameManager;
    private Vector3 velocity;

    // 出生点，供重启复位使用
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    void Start()
    {
        // 优先用单例，找不到再退回场景查找
        gameManager = GameManager.Instance != null ? GameManager.Instance : FindObjectOfType<GameManager>();
    }

    void Update()
    {
        // 缺少 CharacterController 时直接跳过，避免每帧空引用报错
        if (controller == null)
            return;

        //游戏结束锁定移动
        if (gameManager != null && gameManager.isGameOver)
        {
            return;
        }

        //地面检测
        bool isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        //WASD输入
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        Vector3 moveDir = transform.right * horizontal + transform.forward * vertical;
        // 用 sqrMagnitude 比较，省一次开方
        if (moveDir.sqrMagnitude > 1f)
            moveDir.Normalize();

        //水平移动
        controller.Move(moveDir * moveSpeed * Time.deltaTime);

        //施加重力，解决下陷、掉落
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// 复位到出生点（重启时由 GameManager 调用）
    /// </summary>
    public void ResetToSpawn()
    {
        velocity = Vector3.zero;

        // CharacterController 会覆盖直接写入的 transform，
        // 所以传送前必须先把它关掉，改完再打开
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            controller.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
    }
}
