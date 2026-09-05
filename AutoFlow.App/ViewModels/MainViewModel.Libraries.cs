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
    /// Every condition in the selected game's library, as the editable list for the condition
    /// tab. Shares its <see cref="ConditionViewModel"/> instances with
    /// <see cref="AvailableConditions"/>? No — deliberately not: this list is rebuilt from the
    /// library on reload, and reusing the picker's instances would make a cancelled edit visible
    /// in the picker.
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
    public RelayCommand DeleteConditionCommand { get; private set; } = null!;
    public RelayCommand SaveConditionCommand { get; private set; } = null!;
    public RelayCommand RevealConditionFileCommand { get; private set; } = null!;
    public RelayCommand UseWindowAsConditionReferenceCommand { get; private set; } = null!;

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

        LibraryConditions.Clear();
        foreach (var condition in _conditionLibrary.All.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            LibraryConditions.Add(new ConditionViewModel(condition));
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

        Log($"已新建条件 '{id}': {savedPath}");
        Log("下一步: 开始捕获目标窗口 → 填写截图文件名 → 框选匹配区域");
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
