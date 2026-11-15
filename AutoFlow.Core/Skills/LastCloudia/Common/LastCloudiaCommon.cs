using AutoFlow.Core.Models;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Vision;
using OpenCvSharp;

namespace AutoFlow.Core.Skills.LastCloudia.Common;

/// <summary>
/// Shared detection and action helpers for Last Cloudia skills.
/// Translated from CommonActions.ahk. This is a static utility layer (not an ISkill):
/// every method delegates to the services on <see cref="ScriptExecutionContext"/>
/// (TemplateMatch, Input, CoordinateTransform) rather than re-implementing them.
///
/// All coordinates are in the AHK 750x1040 client reference space, used verbatim.
/// </summary>
public static class LastCloudiaCommon
{
    // Shared images live under bin/Skills/LastCloudia/Common/images/ (copied at build time).
    private static readonly string ImageBasePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Skills", "LastCloudia", "Common", "images");

    private const double DefaultThreshold = 0.85;
    private const double DefaultMeanDiff = 30.0;

    private static string GetImagePath(string imageName) => Path.Combine(ImageBasePath, imageName);

    /// <summary>
    /// Check whether a template image (given by full path) is present inside a region
    /// (script coordinates). Mirrors AHK IsImageMatch: crop the region, then match.
    /// Public so skills can reuse the region-crop-and-match primitive with their own images.
    /// </summary>
    public static bool IsImageMatchAt(
        ScriptExecutionContext ctx,
        Mat frame,
        string templatePath,
        int x1, int y1, int x2, int y2,
        double threshold = DefaultThreshold)
    {
        if (!File.Exists(templatePath))
        {
            ctx.Log($"[Common] 模板缺失: {templatePath}");
            return false;
        }

        // Region: script (750x1040) -> actual window/frame pixels
        var windowInfo = ctx.GetWindowInfo();
        var topLeft = ctx.CoordinateTransform.ScriptToWindow(x1, y1, windowInfo);
        var bottomRight = ctx.CoordinateTransform.ScriptToWindow(x2, y2, windowInfo);

        var x = Math.Clamp(Math.Min(topLeft.X, bottomRight.X), 0, frame.Width - 1);
        var y = Math.Clamp(Math.Min(topLeft.Y, bottomRight.Y), 0, frame.Height - 1);
        var right = Math.Clamp(Math.Max(topLeft.X, bottomRight.X), x + 1, frame.Width);
        var bottom = Math.Clamp(Math.Max(topLeft.Y, bottomRight.Y), y + 1, frame.Height);

        using var roi = new Mat(frame, new Rect(x, y, right - x, bottom - y));
        var match = ctx.TemplateMatch.TryFindTemplate(
            roi, templatePath, TemplateMatchOptions.CCoeffNormedNoMask, threshold, DefaultMeanDiff);
        return match != null;
    }

    /// <summary>Overload for Common's own images (resolved under Common/images/).</summary>
    private static bool IsImageMatch(
        ScriptExecutionContext ctx,
        Mat frame,
        string imageName,
        int x1, int y1, int x2, int y2,
        double threshold = DefaultThreshold)
        => IsImageMatchAt(ctx, frame, GetImagePath(imageName), x1, y1, x2, y2, threshold);

    /// <summary>
    /// Move to a button and click it. Mirrors AHK ClickTargetButton(target, delay := 1000):
    /// MouseMove -> Sleep(delay) -> Click. The delay is BEFORE the click (lets the UI settle
    /// after the pointer move), so this is not the same as a single ClickAt call.
    /// Public so skills can reuse this move-wait-click primitive for arbitrary coordinates.
    /// </summary>
    public static async Task ClickTargetButtonAsync(
        ScriptExecutionContext ctx,
        int scriptX, int scriptY,
        int delayMs,
        CancellationToken ct)
    {
        var screen = ctx.ScriptToScreen(scriptX, scriptY);
        ctx.Input.MoveTo(screen.X, screen.Y);
        await Task.Delay(delayMs, ct);
        ctx.Input.Click();
    }

    // === Detection methods (Is* series) — regions from CommonActions.ahk ===

    /// <summary>是否战斗宝箱奖励结算画面</summary>
    public static bool IsBattleEnd(ScriptExecutionContext ctx, Mat frame) =>
        IsImageMatch(ctx, frame, "battle_end_area.png", 169, 205, 292, 270);

    /// <summary>战斗最后的结算界面</summary>
    public static bool IsBattleLastResult(ScriptExecutionContext ctx, Mat frame) =>
        IsImageMatch(ctx, frame, "battle_last_result.png", 100, 900, 625, 945);

    /// <summary>是否是队伍界面</summary>
    public static bool IsSingleTeamPrepare(ScriptExecutionContext ctx, Mat frame) =>
        IsImageMatch(ctx, frame, "team_prepare_area.png", 340, 870, 390, 890);

    /// <summary>是否橙宝玉手动恢复界面</summary>
    public static bool IsStaminaRecover(ScriptExecutionContext ctx, Mat frame) =>
        IsImageMatch(ctx, frame, "stamina_recover_area.png", 265, 200, 465, 235);

    /// <summary>初次通关奖励（结算页面的文字识别区域）</summary>
    public static bool IsFirstClearReward(ScriptExecutionContext ctx, Mat frame) =>
        IsImageMatch(ctx, frame, "first_clear_reward_area.png", 340, 525, 390, 545);

    // === Action methods (Click* series) — button coordinates from CommonActions.ahk ===

    /// <summary>点击队伍准备按钮 (ready_button)</summary>
    public static Task ClickReadyAsync(ScriptExecutionContext ctx, CancellationToken ct) =>
        ClickTargetButtonAsync(ctx, 375, 865, 1000, ct);

    /// <summary>点击返回按钮 (back_button)</summary>
    public static Task ClickBackAsync(ScriptExecutionContext ctx, CancellationToken ct) =>
        ClickTargetButtonAsync(ctx, 80, 960, 1000, ct);

    /// <summary>点击恢复体力按钮 (recover_stamina)</summary>
    public static Task ClickRecoverStaminaAsync(ScriptExecutionContext ctx, CancellationToken ct) =>
        ClickTargetButtonAsync(ctx, 365, 820, 500, ct);

    /// <summary>点击增加体力按钮多次 (add_stamina)，翻译自 ClickAddStamina(count := 4)</summary>
    public static async Task ClickAddStaminaAsync(ScriptExecutionContext ctx, int count = 4, CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            await ClickTargetButtonAsync(ctx, 520, 470, 800, ct);
        }
    }

    /// <summary>
    /// 连点，翻译自 Tools.ahk 的 MultiClick(times := 5, interval := 300)。
    /// 在当前鼠标位置点击，不移动。用于跳过多屏结算。
    /// </summary>
    public static async Task MultiClickAsync(
        ScriptExecutionContext ctx,
        int times = 5,
        int intervalMs = 300,
        CancellationToken ct = default)
    {
        for (int i = 0; i < times; i++)
        {
            ctx.Input.Click();
            await Task.Delay(intervalMs, ct);
        }
    }
}
