using System.Collections.Generic;

/// <summary>
/// 本局排行榜：纯内存数据，刻意不做任何持久化。
/// 关掉程序数据就没了，下次启动从空榜开始，不会留下历史记录。
/// 排序只看分数，不看存活时间；同分按先后顺序排，不引入任何时间因素。
/// </summary>
public static class Leaderboard
{
    /// <summary>内存里最多保留多少条记录。</summary>
    public const int Capacity = 10;

    private static readonly List<int> scores = new List<int>();

    /// <summary>当前已记录的条数。</summary>
    public static int Count { get { return scores.Count; } }

    /// <summary>最高分，没有记录时返回 0。</summary>
    public static int Best { get { return scores.Count > 0 ? scores[0] : 0; } }

    /// <summary>取第 rank 名的分数（rank 从 1 开始），越界返回 0。</summary>
    public static int GetScore(int rank)
    {
        if (rank < 1 || rank > scores.Count)
            return 0;
        return scores[rank - 1];
    }

    /// <summary>
    /// 提交一局分数，返回它的名次（从 1 开始）；没进榜返回 0。
    /// 只按分数从高到低排序。
    /// </summary>
    public static int Submit(int score)
    {
        int index = scores.Count;
        for (int i = 0; i < scores.Count; i++)
        {
            // 严格大于才插到前面：同分保持先来后到，名次完全由分数决定
            if (score > scores[i])
            {
                index = i;
                break;
            }
        }

        scores.Insert(index, score);
        if (scores.Count > Capacity)
        {
            scores.RemoveAt(scores.Count - 1);
        }

        return index < Capacity ? index + 1 : 0;
    }

    /// <summary>
    /// 手动清空。当前没有代码调用它，留给以后做"清空榜单"的按钮。
    /// </summary>
    public static void Clear()
    {
        scores.Clear();
    }
}
