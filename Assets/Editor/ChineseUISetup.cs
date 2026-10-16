#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 一键启用中文 UI。
/// 菜单：Tools / 寻路 / 启用中文 UI（生成字体 + 切换文案）
///
/// 依次做五件事：
///   1. 用 Assets/TextMesh Pro/Fonts 里的中文字体生成 TMP 动态字体资源
///   2. 把它加进 TMP Settings 的 Fallback Font Assets（以后新增文本也能显示中文）
///   3. 把当前场景里所有 TMP 文本的字体换成它
///   4. 把 GameManager 的文案字段改成中文（冠军/亚军/季军…）
///   5. 保存场景
///
/// 可以重复执行：字体资源和回退项已存在时会直接复用。
///
/// 为什么用"动态"字体资源：CJK 字形上万个，静态烘焙要几千字的图集又慢又大；
/// 动态模式只在真正用到的字上按需栅格化，图集不够时 TMP 会自动开新图集。
/// </summary>
public static class ChineseUISetup
{
    const string SourceFontFolder = "Assets/TextMesh Pro/Fonts";
    const string FontAssetPath    = "Assets/TextMesh Pro/Resources/Fonts & Materials/Chinese UI SDF.asset";
    const string TmpSettingsPath  = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    // 采样点数 / 内边距 / 图集边长。
    // 64pt + 2048 图集：单个字形约 76px，一张图集能放 600 多个汉字，HUD 用绰绰有余。
    const int SamplingPointSize = 64;
    const int AtlasPadding      = 6;
    const int AtlasSize         = 2048;

    // 候选源字体，取第一个存在的。
    // 必须是静态 TTF：Unity 2021.3 的 FontEngine 没有设置可变字体轴的 API，
    // 可变字体（VF）会按 fvar 里的默认实例渲染，很容易得到极细的字重。
    static readonly string[] SourceFontCandidates =
    {
        SourceFontFolder + "/SourceHanSansSC-Regular.otf",
        SourceFontFolder + "/NotoSansSC-Regular.ttf",
        SourceFontFolder + "/LXGWWenKai-Regular.ttf",
        SourceFontFolder + "/simhei.ttf",
        SourceFontFolder + "/Deng.ttf",
    };

    [MenuItem("Tools/寻路/启用中文 UI（生成字体 + 切换文案）", false, 1)]
    public static void EnableChineseUI()
    {
        // ---------- 0) 场景检查 ----------
        Scene scene = SceneManager.GetActiveScene();
        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm == null)
        {
            EditorUtility.DisplayDialog("启用中文 UI",
                "当前打开的场景（" + scene.name + "）里找不到 GameManager。\n\n" +
                "请先打开 Assets/Scenes/SampleScene.unity 再执行。", "好");
            return;
        }

        // ---------- 1) 找源字体 ----------
        string fontPath = null;
        foreach (string candidate in SourceFontCandidates)
        {
            if (File.Exists(candidate)) { fontPath = candidate; break; }
        }
        if (fontPath == null)
        {
            EditorUtility.DisplayDialog("启用中文 UI",
                "在 " + SourceFontFolder + " 里没找到可用的中文字体。\n\n" +
                "把任意中文 .ttf 放进去再执行即可，建议命名：\n" +
                "  SourceHanSansSC-Regular.otf（思源黑体）\n" +
                "  NotoSansSC-Regular.ttf（Noto Sans SC 静态版）\n" +
                "  simhei.ttf（黑体）\n\n" +
                "注意不要用可变字体（文件名常带 VF）：Unity 2021.3 无法指定字重，\n" +
                "可变字体会按它自带的默认实例渲染，可能细到看不清。", "好");
            return;
        }

        // ---------- 2) 确保 Include Font Data 打开 ----------
        TrueTypeFontImporter importer = AssetImporter.GetAtPath(fontPath) as TrueTypeFontImporter;
        if (importer != null && !importer.includeFontData)
        {
            importer.includeFontData = true;
            importer.SaveAndReimport();
            Debug.Log("[中文UI] 已为 " + fontPath + " 打开 Include Font Data");
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        if (sourceFont == null)
        {
            EditorUtility.DisplayDialog("启用中文 UI",
                "字体加载失败（可能还在导入中，稍等几秒再试一次）：\n" + fontPath, "好");
            return;
        }

        // ---------- 3) 生成 / 复用 TMP 字体资源 ----------
        TMP_FontAsset fontAsset = CreateOrLoadFontAsset(sourceFont);
        if (fontAsset == null)
        {
            EditorUtility.DisplayDialog("启用中文 UI",
                "TMP 无法从这个字体创建资源（FontEngine.LoadFontFace 失败）。\n\n" +
                "常见原因：字体文件不受支持，或 Include Font Data 没打开。\n" +
                "字体：" + fontPath, "好");
            return;
        }

        // ---------- 4) 加进 TMP Settings 回退列表 ----------
        AddFallbackFont(fontAsset);

        // ---------- 5) 场景里所有 TMP 文本换字体（必须在写中文之前）----------
        int changed = ApplyFontToSceneTexts(fontAsset);

        // ---------- 6) 文案换成中文 ----------
        ApplyChineseLabels(gm);

        // ---------- 7) 保存 ----------
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(string.Format("[中文UI] 完成：源字体 {0}，字体资源 {1}，替换文本 {2} 处，场景已保存",
            fontPath, FontAssetPath, changed));

        EditorUtility.DisplayDialog("启用中文 UI",
            "完成。\n\n" +
            "源字体：" + fontPath + "\n" +
            "字体资源：" + FontAssetPath + "\n" +
            "替换文本：" + changed + " 处\n\n" +
            "场景已保存，直接按 Play 就能看到中文。", "好");
    }

    // ============================================================

    static TMP_FontAsset CreateOrLoadFontAsset(Font source)
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null) return existing;

        EnsureFolder(FontAssetPath);

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(source, SamplingPointSize, AtlasPadding,
            GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
        if (fontAsset == null) return null;

        fontAsset.name = "Chinese UI SDF";
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

        // 图集贴图和材质必须作为子资源一起存进去，否则重开工程就丢了
        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                if (fontAsset.atlasTextures[i] == null) continue;
                fontAsset.atlasTextures[i].name = fontAsset.name + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[i], fontAsset);
            }
        }
        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
    }

    static void EnsureFolder(string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(dir)) return;

        string[] parts = dir.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static void AddFallbackFont(TMP_FontAsset fontAsset)
    {
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
        {
            Debug.LogWarning("[中文UI] 找不到 TMP Settings，跳过回退字体设置（已直接换过场景文本的字体，不影响使用）");
            return;
        }

        SerializedObject so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_fallbackFontAssets");
        if (list == null)
        {
            Debug.LogWarning("[中文UI] TMP Settings 里没有 m_fallbackFontAssets 字段，跳过");
            return;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == fontAsset) return;  // 已经有了
        }

        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fontAsset;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
    }

    static int ApplyFontToSceneTexts(TMP_FontAsset fontAsset)
    {
        // includeInactive: true —— GAME OVER / 结算文本默认是隐藏的，也要一起换
        TMP_Text[] texts = Object.FindObjectsOfType<TMP_Text>(true);
        int changed = 0;

        foreach (TMP_Text text in texts)
        {
            SerializedObject so = new SerializedObject(text);
            SerializedProperty fontProp = so.FindProperty("m_fontAsset");
            if (fontProp == null) continue;
            if (fontProp.objectReferenceValue == fontAsset) continue;

            fontProp.objectReferenceValue = fontAsset;

            // 材质要一起换：留着旧字体的材质会继续指向旧图集，渲染出乱码或空白
            SerializedProperty matProp = so.FindProperty("m_sharedMaterial");
            if (matProp != null) matProp.objectReferenceValue = fontAsset.material;

            // 清掉按旧字体缓存的材质数组
            SerializedProperty arr = so.FindProperty("m_fontSharedMaterials");
            if (arr != null) arr.ClearArray();
            arr = so.FindProperty("m_fontMaterials");
            if (arr != null) arr.ClearArray();

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(text);
            changed++;
        }
        return changed;
    }

    static void ApplyChineseLabels(GameManager gm)
    {
        SerializedObject so = new SerializedObject(gm);

        SetString(so, "timeLabel", "时间");
        SetString(so, "scoreLabel", "分数");
        SetString(so, "finalScoreLabel", "最终分数");
        SetString(so, "rankLabel", "名次");
        SetString(so, "leaderboardHeader", "排行榜");
        SetString(so, "currentRunMark", "本局");

        // 前三名给名号；第四第五留空，GetRankLabel 会自动退回 "4." "5."
        string[] labels = { "冠军", "亚军", "季军", "", "" };
        SerializedProperty arr = so.FindProperty("rankLabels");
        if (arr != null)
        {
            arr.arraySize = labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                arr.GetArrayElementAtIndex(i).stringValue = labels[i];
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(gm);

        // 场景里写死的两个文本也换成中文
        if (gm.gameOverTitle != null)
        {
            TMP_Text title = gm.gameOverTitle.GetComponent<TMP_Text>();
            if (title != null) { title.text = "游戏结束"; EditorUtility.SetDirty(title); }
        }
        if (gm.restartButton != null)
        {
            TMP_Text label = gm.restartButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { label.text = "重新开始"; EditorUtility.SetDirty(label); }
        }
    }

    static void SetString(SerializedObject so, string propertyName, string value)
    {
        SerializedProperty p = so.FindProperty(propertyName);
        if (p != null) p.stringValue = value;
    }
}
#endif
