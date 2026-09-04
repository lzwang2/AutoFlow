using AutoFlow.Core.Scripting.Conditions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoFlow.Core.Scripting.Rules;

/// <summary>
/// 规则模板服务 - 管理可复用的规则模板
/// </summary>
public class RuleTemplateService
{
    private readonly string _templateDirectory;

    public RuleTemplateService(string? templateDirectory = null)
    {
        _templateDirectory = templateDirectory ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "RuleTemplates");

        EnsureTemplateDirectoryExists();
    }

    private void EnsureTemplateDirectoryExists()
    {
        if (!Directory.Exists(_templateDirectory))
        {
            Directory.CreateDirectory(_templateDirectory);
        }
    }

    /// <summary>
    /// 保存规则为模板
    /// </summary>
    public void SaveAsTemplate(Rule rule, string templateName, string? description = null)
    {
        var template = new RuleScript
        {
            // Schema 与 FormatVersion 沿用 RuleScript 的默认值，不在此处重写，
            // 避免多个保存入口写出不一致的值（历史上这里曾写 rule-template.schema.json）
            Metadata = new RuleScriptMetadata
            {
                Name = templateName,
                Description = description ?? $"规则模板: {rule.Name}",
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            },
            Rules = new List<Rule> { rule }
        };

        var fileName = TemplateFileNames.Sanitize(templateName) + TemplateLayout.RuleExtension;
        var filePath = Path.Combine(_templateDirectory, fileName);

        var json = JsonConvert.SerializeObject(template, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 保存规则为模板（指定游戏）
    /// </summary>
    /// <param name="rule">规则对象</param>
    /// <param name="gameName">游戏名称</param>
    /// <param name="fileName">文件名（不含扩展名），通常使用截图名称</param>
    /// <param name="displayName">显示名称，在模板库中显示</param>
    /// <param name="description">可选的描述信息</param>
    /// <param name="condition">
    /// 规则引用的共享条件。传入时会连同条件文件和它的图片一起写进模板库——否则模板到了别的
    /// 机器上只有一个悬空的 ConditionRef。规则无条件（兜底规则）时传 null。
    /// </param>
    /// <returns>写入的模板文件完整路径。</returns>
    public string SaveAsTemplate(
        Rule rule, string gameName, string fileName, string displayName,
        string? description = null, VisualConditionSpec? condition = null)
    {
        // 规则模板写入 <Game>/Rules/（Spec §6.4）；images/ 位置不动，
        // 因为所有模板的图片路径都写成 "<Game>/images/x.png"，挪走会让这些字符串全部失效。
        var rulesDir = TemplateLayout.DirectoryFor(_templateDirectory, gameName, TemplateKind.Rule);
        var imagesDir = TemplateLayout.ImagesDirectoryFor(_templateDirectory, gameName);
        Directory.CreateDirectory(rulesDir);
        Directory.CreateDirectory(imagesDir);

        // 图片现在属于条件，不属于规则：规则只带一个 ConditionRef，所以要导出的是条件的图片。
        if (condition is not null)
        {
            SaveConditionAlongside(condition, gameName, imagesDir);
        }

        var template = new RuleScript
        {
            Metadata = new RuleScriptMetadata
            {
                Name = displayName,
                Description = description,
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd")
            },
            Rules = new List<Rule> { rule },
            IdleTickMs = 500,
            OuterWidth = 750,
            OuterHeight = 1040
        };

        var sanitizedFileName = TemplateFileNames.Sanitize(fileName) + TemplateLayout.RuleExtension;
        var filePath = Path.Combine(rulesDir, sanitizedFileName);

        var json = JsonConvert.SerializeObject(template, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        File.WriteAllText(filePath, json);
        return filePath;
    }

    /// <summary>
    /// 把规则引用的条件写进模板库，并把它的图片复制到 &lt;Game&gt;/images/。
    ///
    /// 条件文件里存的路径改写成库相对形式（"&lt;Game&gt;/images/x.png"），这样模板整体拷到另一台
    /// 机器上仍然能解析——直接沿用编辑时的绝对路径会只在本机有效。
    /// </summary>
    private void SaveConditionAlongside(VisualConditionSpec condition, string gameName, string imagesDir)
    {
        // 用 EffectiveTemplatePath 找源图：编辑器刚截的图是绝对路径，从库里读出来的是相对路径
        // 加一个已解析的绝对路径，两种都要能定位到。
        var source = condition.EffectiveTemplatePath;
        var exported = new VisualConditionSpec
        {
            Id = condition.Id,
            Name = condition.Name,
            Description = condition.Description,
            TemplatePath = condition.TemplatePath,
            RegionX1 = condition.RegionX1,
            RegionY1 = condition.RegionY1,
            RegionX2 = condition.RegionX2,
            RegionY2 = condition.RegionY2,
            MatchThreshold = condition.MatchThreshold,
            MeanDiffThreshold = condition.MeanDiffThreshold,
            ReferenceOuterWidth = condition.ReferenceOuterWidth,
            ReferenceOuterHeight = condition.ReferenceOuterHeight,
            Tags = new List<string>(condition.Tags),
            Category = condition.Category
        };

        if (!string.IsNullOrWhiteSpace(source))
        {
            var copied = CopyTemplateImage(source, imagesDir, gameName);
            if (copied is not null)
            {
                exported.TemplatePath = copied;
            }
        }

        new ConditionLibrary(_templateDirectory).Save(exported, gameName);
    }

    /// <summary>
    /// 保存多个规则为模板
    /// </summary>
    public void SaveAsTemplate(IEnumerable<Rule> rules, string templateName, string? description = null)
    {
        var template = new RuleScript
        {
            // 同上：Schema / FormatVersion 由 RuleScript 默认值统一提供
            Metadata = new RuleScriptMetadata
            {
                Name = templateName,
                Description = description ?? $"规则模板: {templateName}",
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            },
            Rules = new List<Rule>(rules)
        };

        var fileName = TemplateFileNames.Sanitize(templateName) + TemplateLayout.RuleExtension;
        var filePath = Path.Combine(_templateDirectory, fileName);

        var json = JsonConvert.SerializeObject(template, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 列出所有模板
    /// </summary>
    public List<RuleTemplateInfo> ListTemplates()
    {
        var templates = new List<RuleTemplateInfo>();

        if (!Directory.Exists(_templateDirectory))
        {
            return templates;
        }

        // Recursive so templates are found both in the new <Game>/Rules/ layout and in the
        // pre-migration <Game>/ layout — the library must stay browsable across Phase 6's move.
        var files = Directory.GetFiles(
            _templateDirectory, "*" + TemplateLayout.RuleExtension, SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var script = JsonConvert.DeserializeObject<RuleScript>(json);

                if (script?.Rules != null)
                {
                    templates.Add(new RuleTemplateInfo
                    {
                        FilePath = file,
                        Name = script.Metadata?.Name ?? Path.GetFileNameWithoutExtension(file),
                        Description = script.Metadata?.Description ?? "",
                        Author = script.Metadata?.Author ?? "",
                        CreatedAt = script.Metadata?.CreatedAt ?? "",
                        RuleCount = script.Rules.Count
                    });
                }
            }
            catch
            {
                // 忽略无效的模板文件
            }
        }

        return templates.OrderByDescending(t => t.CreatedAt).ToList();
    }

    /// <summary>
    /// 从模板加载规则
    /// </summary>
    public List<Rule> LoadRulesFromTemplate(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var script = JsonConvert.DeserializeObject<RuleScript>(json);

        if (script?.Rules == null)
        {
            throw new InvalidOperationException("模板文件格式无效");
        }

        // 模板导入是脚本进入编辑器的第二条入口，闸门必须和 RuleScriptService.Load 一样在这里
        // 拦一次。少了这一道，v1 模板的内联条件字段（TemplatePath / 区域 / 阈值）会被静默丢弃
        // ——新模型已经没有这些属性了——而 ConditionRef 是空的，于是那条规则被当成无条件兜底
        // 规则导入：不报错，但每个 tick 都命中并执行动作。
        ScriptFormatVersion.Validate(script.FormatVersion, filePath);

        // 返回规则的深拷贝（避免修改模板文件）
        var clonedJson = JsonConvert.SerializeObject(script.Rules);
        return JsonConvert.DeserializeObject<List<Rule>>(clonedJson) ?? new List<Rule>();
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    public void DeleteTemplate(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// 获取模板目录路径
    /// </summary>
    public string GetTemplateDirectory() => _templateDirectory;

    /// <summary>
    /// 获取所有游戏列表
    /// </summary>
    public List<string> GetGames() => new TemplateIndex(_templateDirectory).Games().ToList();

    /// <summary>
    /// 复制模板图片到目标目录
    /// </summary>
    private string? CopyTemplateImage(string sourceTemplatePath, string targetImagesDir, string gameName)
    {
        try
        {
            // 尝试多种可能的源路径
            string? resolvedSourcePath = null;

            // 1. 绝对路径
            if (Path.IsPathRooted(sourceTemplatePath) && File.Exists(sourceTemplatePath))
            {
                resolvedSourcePath = sourceTemplatePath;
            }
            // 2. 相对于 RuleTemplates 的路径（如 "LastCloudia/images/xxx.png"）
            else
            {
                // 注意：这里只查模板根，不用 TemplatePathResolver 的两级回退——
                // 此处是「把外部图片复制进模板库」的入口，源图不该按引用文件目录去猜。
                var relPath = Path.Combine(_templateDirectory, sourceTemplatePath);
                if (File.Exists(relPath))
                {
                    resolvedSourcePath = relPath;
                }
            }

            if (resolvedSourcePath == null)
            {
                return null; // 源文件不存在，保持原路径
            }

            var fileName = Path.GetFileName(resolvedSourcePath);
            var targetPath = Path.Combine(targetImagesDir, fileName);

            // 复制文件（如果不存在或内容不同）
            if (!File.Exists(targetPath) || !FilesAreEqual(resolvedSourcePath, targetPath))
            {
                File.Copy(resolvedSourcePath, targetPath, overwrite: true);
            }

            // 返回相对路径格式：GameName/images/xxx.png
            // 只依赖 TemplatePathResolver 的第 2 级回退（相对模板根），
            // 因此规则文件挪进 Rules/ 后这个字符串依然有效（Spec §6.4）。
            return $"{gameName}/{TemplateLayout.ImagesDirectoryName}/{fileName}";
        }
        catch
        {
            return null; // 复制失败，保持原路径
        }
    }

    private static bool FilesAreEqual(string path1, string path2)
    {
        var file1 = new FileInfo(path1);
        var file2 = new FileInfo(path2);

        if (file1.Length != file2.Length)
            return false;

        // 长度相同时必须比较内容：模板图片尺寸接近，仅凭长度相等就判定为同一张，
        // 会导致内容不同的图被静默跳过复制，模板引用到错误的图片。
        const int bufferSize = 64 * 1024;
        using var stream1 = file1.OpenRead();
        using var stream2 = file2.OpenRead();

        var buffer1 = new byte[bufferSize];
        var buffer2 = new byte[bufferSize];

        while (true)
        {
            var read1 = ReadFully(stream1, buffer1);
            var read2 = ReadFully(stream2, buffer2);

            if (read1 != read2)
                return false;

            if (read1 == 0)
                return true;

            if (!buffer1.AsSpan(0, read1).SequenceEqual(buffer2.AsSpan(0, read2)))
                return false;
        }
    }

    /// <summary>
    /// 尽量填满缓冲区，避免 Read 返回短读时被误判为文件长度不同。
    /// </summary>
    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

}

/// <summary>
/// 规则模板信息
/// </summary>
public class RuleTemplateInfo
{
    public string FilePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public int RuleCount { get; set; }

    /// <summary>
    /// Just the filename of <see cref="FilePath"/>, so a listed template can be matched against
    /// a file on disk. Metadata names are Chinese display names while filenames are ASCII slugs,
    /// so the name alone does not tell the user which file a row came from.
    /// </summary>
    public string FileName => string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath);

    public string DisplayText => $"{Name} ({RuleCount} 个规则)";
}
