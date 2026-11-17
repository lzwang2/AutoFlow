using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using AutoFlow.App.Common;
using AutoFlow.App.Views;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// The two shared-object editors: the image condition library and the action block library.
///
/// <para>Separate from the rest of <see cref="MainViewModel"/> because these tabs own the write
/// side of the shared libraries, while the script editors only reference them. Keeping that
/// split visible in the file layout is the point — a condition edited from two places is a
/// condition nobody can reason about, which is why the editors lost their inline condition
/// fields when this file appeared.</para>
/// </summary>
public sealed partial class MainViewModel
{
    // === Condition library tab ===

    /// <summary>
    /// The condition editor's working set: only what the user explicitly created, imported or
    /// copied, not the whole library.
    ///
    /// <para>Deliberately not the full library. <c>_conditionLibrary</c> still loads every
    /// condition — the reference pickers and runtime resolution need it — but a list that grows
    /// with the library becomes unusable for editing once a game has a few dozen conditions.
    /// The working set is per-session and cleared on a game switch.</para>
    ///
    /// <para>Shares its <see cref="ConditionViewModel"/> instances with
    /// <see cref="AvailableConditions"/>? No — deliberately not: reusing the picker's instances
    /// would make a cancelled edit visible in the picker.</para>
    /// </summary>
    public ObservableCollection<ConditionViewModel> LibraryConditions { get; } = new();

    private ConditionViewModel? _selectedLibraryCondition;

    /// <summary>The condition being edited in the condition library tab.</summary>
    public ConditionViewModel? SelectedLibraryCondition
    {
        get => _selectedLibraryCondition;
        set
        {
            if (!SetProperty(ref _selectedLibraryCondition, value))
                return;

            // The capture workflow writes into whatever is selected here, so a selection change
            // must invalidate a crop that was framed for the previous one.
            if (IsConfirmingRegion)
            {
                DiscardPendingRegion();
                IsConfirmingRegion = false;
            }

            PendingImageName = SuggestImageNameFor(value);
            OnPropertyChanged(nameof(SelectedConditionUsageSummary));
            CommandManagerRefresh();
        }
    }

    /// <summary>
    /// Which rules and steps in the currently open script reference the selected condition.
    ///
    /// <para>Shown because editing a shared condition changes every reference at once, and this
    /// is the only warning the user gets before doing it. Scoped to the open script on purpose:
    /// scanning every file in the library on each selection change would be a disk walk per
    /// click, and the open script is the one whose behaviour is about to change under them.</para>
    /// </summary>
    public string SelectedConditionUsageSummary
    {
        get
        {
            if (SelectedLibraryCondition is not { } condition)
                return "未选择条件";

            var ruleNames = Rules
                .Where(r => string.Equals(r.ConditionRef, condition.Id, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Name)
                .ToList();

            var stepCount = Steps.Count(
                s => string.Equals(s.ConditionRef, condition.Id, StringComparison.OrdinalIgnoreCase));

            if (ruleNames.Count == 0 && stepCount == 0)
                return "当前打开的脚本没有引用这个条件";

            var parts = new List<string>();
            if (ruleNames.Count > 0)
                parts.Add($"{ruleNames.Count} 条规则 ({string.Join(", ", ruleNames)})");
            if (stepCount > 0)
                parts.Add($"{stepCount} 个线性步骤");

            return $"当前脚本中有 {string.Join("、", parts)} 引用它，改动会同时影响它们";
        }
    }

    /// <summary>
    /// File name for the next capture. Named separately from the old TemplateName because it now
    /// means only one thing — the .png a crop is written to — instead of doubling as a rule
    /// template's file name.
    /// </summary>
    private string _pendingImageName = string.Empty;
    public string PendingImageName
    {
        get => _pendingImageName;
        set { if (SetProperty(ref _pendingImageName, value)) CommandManagerRefresh(); }
    }

    public RelayCommand NewConditionCommand { get; private set; } = null!;
    public RelayCommand ImportConditionCommand { get; private set; } = null!;
    public RelayCommand CopyConditionCommand { get; private set; } = null!;
    public RelayCommand CloseConditionCommand { get; private set; } = null!;
    public RelayCommand DeleteConditionCommand { get; private set; } = null!;
    public RelayCommand SaveConditionCommand { get; private set; } = null!;
    public RelayCommand RevealConditionFileCommand { get; private set; } = null!;
    public RelayCommand UseWindowAsConditionReferenceCommand { get; private set; } = null!;

    private bool CanQuickRecaptureRegion =>
        IsCapturing && !IsRunning && SelectedLibraryCondition?.Model.HasRegion == true;

    /// <summary>Captures the currently selected condition's stored region without mouse selection.</summary>
    private void QuickRecaptureRegion()
    {
        if (!CanQuickRecaptureRegion || SelectedLibraryCondition is not { } selected)
            return;

        try
        {
            using var frame = _captureService.CaptureFrame();
            if (frame is null) throw new InvalidOperationException("无法获取画面帧。");
            var path = AutoFlow.App.Services.ConditionImageRecapture.Save(frame, selected.Model, TemplatePathResolver.TemplateRoot);
            Log($"已按条件 '{selected.Id}' 的现有区域快速截图并覆盖保存: {path}");
            MessageBox.Show($"已按当前区域重新截图并覆盖：\n{path}", "截图成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"快速截图失败: {ex.Message}");
            MessageBox.Show(ex.Message, "截图失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // === Action block library tab ===

    public ObservableCollection<ActionBlockViewModel> LibraryActionBlocks { get; } = new();

    private ActionBlockViewModel? _selectedLibraryActionBlock;

    public ActionBlockViewModel? SelectedLibraryActionBlock
    {
        get => _selectedLibraryActionBlock;
        set
        {
            if (SetProperty(ref _selectedLibraryActionBlock, value))
            {
                SelectedBlockStep = value?.Steps.FirstOrDefault();
                CommandManagerRefresh();
            }
        }
    }

    private StepViewModel? _selectedBlockStep;

    /// <summary>The step being edited inside the selected action block.</summary>
    public StepViewModel? SelectedBlockStep
    {
        get => _selectedBlockStep;
        set { if (SetProperty(ref _selectedBlockStep, value)) CommandManagerRefresh(); }
    }

    /// <summary>The selected block step's action, for the parameter panel. Null for a block ref.</summary>
    public InstructionViewModel? SelectedBlockAction => SelectedBlockStep?.Action;

    public RelayCommand NewActionBlockCommand { get; private set; } = null!;
    public RelayCommand DeleteActionBlockCommand { get; private set; } = null!;
    public RelayCommand SaveActionBlockCommand { get; private set; } = null!;
    public RelayCommand RevealActionBlockFileCommand { get; private set; } = null!;
    public RelayCommand AddBlockStepClickCommand { get; private set; } = null!;
    public RelayCommand AddBlockStepWaitTimeCommand { get; private set; } = null!;
    public RelayCommand AddBlockStepWaitImageCommand { get; private set; } = null!;
    public RelayCommand RemoveBlockStepCommand { get; private set; } = null!;
    public RelayCommand MoveBlockStepUpCommand { get; private set; } = null!;
    public RelayCommand MoveBlockStepDownCommand { get; private set; } = null!;

    private void InitializeLibraryCommands()
    {
        NewConditionCommand = new RelayCommand(NewCondition);
        ImportConditionCommand = new RelayCommand(ImportConditions);
        CopyConditionCommand = new RelayCommand(CopyCondition, () => SelectedLibraryCondition != null);
        CloseConditionCommand = new RelayCommand(CloseCondition, () => SelectedLibraryCondition != null);
        DeleteConditionCommand = new RelayCommand(DeleteCondition, () => SelectedLibraryCondition != null);
        SaveConditionCommand = new RelayCommand(SaveLibraryCondition, () => SelectedLibraryCondition != null);
        RevealConditionFileCommand = new RelayCommand(
            RevealConditionFile, () => SelectedLibraryCondition != null);
        UseWindowAsConditionReferenceCommand = new RelayCommand(
            UseWindowAsConditionReference, () => SelectedLibraryCondition != null);

        NewActionBlockCommand = new RelayCommand(NewActionBlock);
        DeleteActionBlockCommand = new RelayCommand(
            DeleteActionBlock, () => SelectedLibraryActionBlock != null);
        SaveActionBlockCommand = new RelayCommand(
            SaveLibraryActionBlock, () => SelectedLibraryActionBlock != null);
        RevealActionBlockFileCommand = new RelayCommand(
            RevealActionBlockFile, () => SelectedLibraryActionBlock != null);

        AddBlockStepClickCommand = new RelayCommand(
            () => AddBlockStep(CommandType.Click), () => SelectedLibraryActionBlock != null);
        AddBlockStepWaitTimeCommand = new RelayCommand(
            () => AddBlockStep(CommandType.WaitTime), () => SelectedLibraryActionBlock != null);
        AddBlockStepWaitImageCommand = new RelayCommand(
            () => AddBlockStep(CommandType.WaitImage), () => SelectedLibraryActionBlock != null);
        RemoveBlockStepCommand = new RelayCommand(RemoveBlockStep, () => SelectedBlockStep != null);
        MoveBlockStepUpCommand = new RelayCommand(() => MoveBlockStep(-1), () => CanMoveBlockStep(-1));
        MoveBlockStepDownCommand = new RelayCommand(() => MoveBlockStep(1), () => CanMoveBlockStep(1));
    }

    /// <summary>
    /// Rebuilds both library tabs from the loaded libraries, preserving the selection by Id.
    /// Called from <see cref="LoadSharedLibraries"/>, so the tabs never show a game's objects
    /// after the user has switched away from it.
    /// </summary>
    private void RebuildLibraryTabs()
    {
        var previousConditionId = SelectedLibraryCondition?.Id;
        var previousBlockId = SelectedLibraryActionBlock?.Id;

        // Only the conditions already in the working set are rebuilt, and only those that still
        // exist in the reloaded library. Re-adding everything here is what made the tab unusable:
        // the working set is the user's choice, and a library reload must not undo it.
        var openIds = LibraryConditions.Select(c => c.Id).ToList();
        LibraryConditions.Clear();
        foreach (var id in openIds)
        {
            if (_conditionLibrary.TryGet(id, out var reloaded) && reloaded is not null)
            {
                LibraryConditions.Add(new ConditionViewModel(reloaded));
            }
        }

        LibraryActionBlocks.Clear();
        foreach (var block in _actionBlockLibrary.All.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            LibraryActionBlocks.Add(new ActionBlockViewModel(block, LookupCondition));
        }

        SelectedLibraryCondition = previousConditionId is null
            ? LibraryConditions.FirstOrDefault()
            : LibraryConditions.FirstOrDefault(
                  c => string.Equals(c.Id, previousConditionId, StringComparison.OrdinalIgnoreCase))
              ?? LibraryConditions.FirstOrDefault();

        OnPropertyChanged(nameof(ConditionWorkingSetSummary));

        SelectedLibraryActionBlock = previousBlockId is null
            ? LibraryActionBlocks.FirstOrDefault()
            : LibraryActionBlocks.FirstOrDefault(
                  b => string.Equals(b.Id, previousBlockId, StringComparison.OrdinalIgnoreCase))
              ?? LibraryActionBlocks.FirstOrDefault();
    }

    /// <summary>
    /// Suggests the image file name for a condition, so a re-capture overwrites the condition's
    /// own image instead of silently creating a second file the condition does not point at.
    /// </summary>
    private static string SuggestImageNameFor(ConditionViewModel? condition)
        => string.IsNullOrWhiteSpace(condition?.TemplatePath)
            ? string.Empty
            : Path.GetFileName(condition.TemplatePath)!;

    private void NewCondition()
    {
        var id = TextPromptDialog.Show(
            "新建图像条件",
            "条件 Id:",
            $"{SelectedGame}.",
            "Id 是脚本引用这个条件的名字，保存后不能改（改了会让引用它的规则失效）。" +
            "习惯写法: <游戏>.<含义>，例如 LastCloudia.NetworkError");

        if (id is null) return;

        if (_conditionLibrary.TryGet(id, out _))
        {
            MessageBox.Show(
                $"条件 '{id}' 已存在。请换一个 Id，或直接在列表里选中它编辑。",
                "Id 重复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Written to disk immediately: a condition that exists only in this list can be
        // referenced by a rule which is then saved, leaving a reference the loader rejects.
        // The reference space defaults to the current window size, which is what any capture
        // taken right now would be measured against.
        var condition = new VisualConditionSpec
        {
            Id = id,
            Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
            ReferenceOuterWidth = ResizeWidth,
            ReferenceOuterHeight = ResizeHeight
        };

        if (!TrySaveCondition(condition, out var savedPath))
            return;

        var vm = new ConditionViewModel(condition);
        LibraryConditions.Add(vm);
        SelectedLibraryCondition = vm;
        RebuildAvailableConditions();
        OnPropertyChanged(nameof(ConditionWorkingSetSummary));

        Log($"已新建条件 '{id}': {savedPath}");
        Log("下一步: 开始捕获目标窗口 → 填写截图文件名 → 框选匹配区域");
    }

    /// <summary>
    /// Describes the working set, so an empty list reads as "nothing opened yet" rather than
    /// "the library is empty" — those look identical without it.
    /// </summary>
    public string ConditionWorkingSetSummary
    {
        get
        {
            var total = _conditionLibrary.All.Count;
            return LibraryConditions.Count == 0
                ? $"编辑列表为空。库中共 {total} 个条件，用「导入已有条件」挑要改的，或「新建条件」。"
                : $"正在编辑 {LibraryConditions.Count} 个条件（库中共 {total} 个）";
        }
    }

    /// <summary>
    /// Adds existing library conditions to the working set. Multi-select, because editing a
    /// group of related conditions in one sitting is the normal case.
    /// </summary>
    private void ImportConditions()
    {
        var openIds = LibraryConditions.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = _conditionLibrary.All
            .Where(c => !openIds.Contains(c.Id))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new LibraryPickerItem
            {
                Id = c.Id,
                Name = c.Name,
                Detail = $"{(string.IsNullOrWhiteSpace(c.TemplatePath) ? "无图片" : Path.GetFileName(c.TemplatePath))}"
                         + $" · {(c.HasRegion ? "有区域" : "整帧匹配")}"
                         + $" · 参照 {c.ReferenceOuterWidth}×{c.ReferenceOuterHeight}"
            })
            .ToList();

        if (candidates.Count == 0)
        {
            MessageBox.Show(
                LibraryConditions.Count > 0
                    ? $"游戏 '{SelectedGame}' 的所有条件都已经在编辑列表里了。"
                    : $"游戏 '{SelectedGame}' 的条件库是空的，请先用「新建条件」创建。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var chosen = LibraryPickerDialog.Show(
            "导入已有条件", $"选择要编辑的条件（{SelectedGame}）:", candidates,
            "勾选的条件会加入编辑列表。编辑并保存后会直接写回库文件，影响所有引用它的规则和步骤。");

        if (chosen is null) return;

        ConditionViewModel? last = null;
        foreach (var id in chosen)
        {
            if (!_conditionLibrary.TryGet(id, out var condition) || condition is null)
                continue;

            var vm = new ConditionViewModel(condition);
            LibraryConditions.Add(vm);
            last = vm;
        }

        if (last is not null) SelectedLibraryCondition = last;
        OnPropertyChanged(nameof(ConditionWorkingSetSummary));
        Log($"已导入 {chosen.Count} 个条件到编辑列表");
    }

    /// <summary>
    /// Copies the selected condition under a new Id, to start from a working condition instead
    /// of an empty one.
    /// </summary>
    private void CopyCondition()
    {
        if (SelectedLibraryCondition is not { } source) return;

        var id = TextPromptDialog.Show(
            "复制条件", "新条件 Id:", $"{source.Id}_copy",
            "会复制图片路径、区域、阈值和参照空间。Id 保存后不能改。");

        if (id is null) return;

        if (_conditionLibrary.TryGet(id, out _))
        {
            MessageBox.Show(
                $"条件 '{id}' 已存在。请换一个 Id。",
                "Id 重复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Deep copy via the persistence form: a field-by-field copy would share the Tags list,
        // and any field added to VisualConditionSpec later would be silently dropped here.
        var json = JsonConvert.SerializeObject(source.Model);
        var copy = JsonConvert.DeserializeObject<VisualConditionSpec>(json);
        if (copy is null)
        {
            MessageBox.Show("复制条件失败: 无法序列化源条件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        copy.Id = id;
        copy.Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;

        // ResolvedTemplatePath is [JsonIgnore], so the round trip drops it. Carry it over or the
        // copy shows no preview until the library is reloaded.
        copy.ResolvedTemplatePath = source.Model.ResolvedTemplatePath;

        if (!TrySaveCondition(copy, out var savedPath))
            return;

        var vm = new ConditionViewModel(copy);
        LibraryConditions.Add(vm);
        SelectedLibraryCondition = vm;
        RebuildAvailableConditions();
        OnPropertyChanged(nameof(ConditionWorkingSetSummary));

        // The copy points at the source's image. Suggesting a fresh name stops a re-capture from
        // overwriting the image the source condition is still using.
        PendingImageName = SuggestCopyImageNameFor(copy);

        Log($"已复制条件 '{source.Id}' → '{id}': {savedPath}");
        if (!string.IsNullOrWhiteSpace(copy.TemplatePath))
        {
            Log($"注意: 副本仍指向原图片 {copy.TemplatePath}。重新框选前请确认截图文件名，避免覆盖原条件的图。");
        }
    }

    /// <summary>
    /// Removes a condition from the working set without touching the library or disk. The
    /// counterpart to import: the list is the user's scratch space, not the library itself.
    /// </summary>
    private void CloseCondition()
    {
        if (SelectedLibraryCondition is not { } condition) return;

        var index = LibraryConditions.IndexOf(condition);
        LibraryConditions.Remove(condition);
        SelectedLibraryCondition = LibraryConditions.Count == 0
            ? null
            : LibraryConditions[Math.Clamp(index, 0, LibraryConditions.Count - 1)];

        OnPropertyChanged(nameof(ConditionWorkingSetSummary));
        Log($"已从编辑列表移出条件 '{condition.Id}'（文件未改动）");
    }

    /// <summary>
    /// An image name derived from the copy's own Id, so re-framing a copy writes a new png
    /// instead of overwriting the one the source condition points at.
    /// </summary>
    private static string SuggestCopyImageNameFor(VisualConditionSpec copy)
    {
        var slug = copy.Id.Replace('.', '_');
        var extension = string.IsNullOrWhiteSpace(copy.TemplatePath)
            ? ".png"
            : Path.GetExtension(copy.TemplatePath);
        return slug + (string.IsNullOrWhiteSpace(extension) ? ".png" : extension);
    }

    private void DeleteCondition()
    {
        if (SelectedLibraryCondition is not { } condition) return;

        // Names the references it can see. It cannot see other scripts in the library, so the
        // wording says "当前脚本" rather than implying the check was exhaustive.
        var usage = SelectedConditionUsageSummary;
        var answer = MessageBox.Show(
            $"删除条件 '{condition.Id}'？\n\n{usage}\n\n" +
            "引用它的规则会变成「引用了不存在的条件」，运行时会报错。\n" +
            "条件文件会从磁盘删除，图片不会被删除。",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (!_conditionLibrary.Delete(condition.Id))
            {
                Log($"条件 '{condition.Id}' 不在已加载的条件库中，未删除任何文件");
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除条件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LibraryConditions.Remove(condition);
        SelectedLibraryCondition = LibraryConditions.FirstOrDefault();
        RebuildAvailableConditions();
        RefreshConditionDependentViews();
        OnPropertyChanged(nameof(ConditionWorkingSetSummary));

        Log($"已删除条件 '{condition.Id}'");
    }

    /// <summary>
    /// Persists the edited condition. Explicit rather than automatic on every keystroke: the
    /// condition is shared, so a half-typed threshold must not reach the file other rules read.
    /// </summary>
    private void SaveLibraryCondition()
    {
        if (SelectedLibraryCondition is not { } condition) return;

        if (!TrySaveCondition(condition.Model, out var savedPath))
            return;

        condition.RefreshAll();
        RefreshConditionDependentViews();
        RebuildAvailableConditions();

        Log($"已保存条件 '{condition.Id}': {savedPath}");
    }

    private void RevealConditionFile()
    {
        if (SelectedLibraryCondition is not { } condition) return;
        RevealInExplorer(_conditionLibrary.SourceFileOf(condition.Id), $"条件 '{condition.Id}'");
    }

    /// <summary>
    /// Stamps the current window's outer size onto the condition as its reference space, for
    /// conditions whose region was measured elsewhere (a hand-written file, or a migrated one).
    /// </summary>
    private void UseWindowAsConditionReference()
    {
        if (SelectedLibraryCondition is not { } condition) return;

        condition.ReferenceOuterWidth = ResizeWidth;
        condition.ReferenceOuterHeight = ResizeHeight;
        Log($"条件 '{condition.Id}' 的参照空间已设为 {ResizeWidth}×{ResizeHeight}，记得点保存");
    }

    // === Action blocks ===

    private void NewActionBlock()
    {
        var id = TextPromptDialog.Show(
            "新建动作块",
            "动作块 Id:",
            $"{SelectedGame}.",
            "Id 是脚本引用这个动作块的名字，保存后不能改。" +
            "习惯写法: <游戏>.<含义>，例如 LastCloudia.CloseDialog");

        if (id is null) return;

        if (_actionBlockLibrary.TryGet(id, out _))
        {
            MessageBox.Show(
                $"动作块 '{id}' 已存在。请换一个 Id，或直接在列表里选中它编辑。",
                "Id 重复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var block = new ActionBlock
        {
            Id = id,
            Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id
        };

        try
        {
            var path = _actionBlockLibrary.Save(block, SelectedGame);
            Log($"已新建动作块 '{id}': {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"新建动作块失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var vm = new ActionBlockViewModel(block, LookupCondition);
        LibraryActionBlocks.Add(vm);
        SelectedLibraryActionBlock = vm;
        RebuildAvailableActionBlocks();
    }

    private void DeleteActionBlock()
    {
        if (SelectedLibraryActionBlock is not { } block) return;

        var referencingSteps = Steps.Count(
            s => string.Equals(s.BlockId, block.Id, StringComparison.OrdinalIgnoreCase));
        var usage = referencingSteps > 0
            ? $"当前打开的脚本中有 {referencingSteps} 个步骤引用它。"
            : "当前打开的脚本没有引用它。";

        var answer = MessageBox.Show(
            $"删除动作块 '{block.Id}'？\n\n{usage}\n\n引用它的步骤运行时会报错。",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (!_actionBlockLibrary.Delete(block.Id))
            {
                Log($"动作块 '{block.Id}' 不在已加载的动作块库中，未删除任何文件");
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除动作块失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LibraryActionBlocks.Remove(block);
        SelectedLibraryActionBlock = LibraryActionBlocks.FirstOrDefault();
        RebuildAvailableActionBlocks();

        foreach (var step in Steps)
        {
            step.RefreshConditionView();
        }

        Log($"已删除动作块 '{block.Id}'");
    }

    private void SaveLibraryActionBlock()
    {
        if (SelectedLibraryActionBlock is not { } block) return;

        try
        {
            // ApplyToModel rewrites Steps and ConditionRefs from the edited list, so the block's
            // declared condition dependencies stay in step with the steps that actually use them.
            block.ApplyToModel();
            var path = _actionBlockLibrary.Save(block.Model, SelectedGame);

            var missing = _actionBlockLibrary.ValidateConditionRefs(block.Model, _conditionLibrary);
            if (missing.Count > 0)
            {
                Log($"[警告] 动作块 '{block.Id}' 依赖的条件缺失: {string.Join(", ", missing)}");
            }

            RebuildAvailableActionBlocks();
            foreach (var step in Steps)
            {
                step.RefreshConditionView();
            }

            Log($"已保存动作块 '{block.Id}' ({block.Steps.Count} 步): {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存动作块失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RevealActionBlockFile()
    {
        if (SelectedLibraryActionBlock is not { } block) return;
        RevealInExplorer(_actionBlockLibrary.SourceFileOf(block.Id), $"动作块 '{block.Id}'");
    }

    private void AddBlockStep(CommandType type)
    {
        if (SelectedLibraryActionBlock is not { } block) return;

        var step = StepViewModel.ForAction(
            new Instruction { CommandType = type }, LookupCondition, LookupActionBlock);

        var index = SelectedBlockStep is null
            ? block.Steps.Count
            : block.Steps.IndexOf(SelectedBlockStep) + 1;

        block.Steps.Insert(Math.Clamp(index, 0, block.Steps.Count), step);
        SelectedBlockStep = step;
    }

    private void RemoveBlockStep()
    {
        if (SelectedLibraryActionBlock is not { } block || SelectedBlockStep is not { } step) return;

        var index = block.Steps.IndexOf(step);
        block.Steps.Remove(step);
        SelectedBlockStep = block.Steps.Count == 0
            ? null
            : block.Steps[Math.Clamp(index, 0, block.Steps.Count - 1)];
    }

    private bool CanMoveBlockStep(int delta)
    {
        if (SelectedLibraryActionBlock is not { } block || SelectedBlockStep is not { } step)
            return false;

        var target = block.Steps.IndexOf(step) + delta;
        return target >= 0 && target < block.Steps.Count;
    }

    private void MoveBlockStep(int delta)
    {
        if (!CanMoveBlockStep(delta)) return;

        var block = SelectedLibraryActionBlock!;
        var step = SelectedBlockStep!;
        var index = block.Steps.IndexOf(step);

        block.Steps.Move(index, index + delta);
        SelectedBlockStep = step;
        CommandManagerRefresh();
    }

    /// <summary>
    /// Selects a file in Explorer, reporting the reason when there is nothing to select.
    /// A silent no-op here reads as a broken button.
    /// </summary>
    private void RevealInExplorer(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(
                $"找不到 {what} 的文件。它可能还没保存过，或已在磁盘上被删除。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"打开文件位置失败: {ex.Message}");
        }
    }
}
