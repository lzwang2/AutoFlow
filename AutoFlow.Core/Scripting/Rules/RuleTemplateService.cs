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
            Schema = "https://autoflow.dev/rule-template.schema.json",
            FormatVersion = 1,
            Metadata = new RuleScriptMetadata
            {
                Name = templateName,
                Description = description ?? $"规则模板: {rule.Name}",
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            },
            Rules = new List<Rule> { rule }
        };

        var fileName = SanitizeFileName(templateName) + ".rules.json";
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
    public void SaveAsTemplate(Rule rule, string gameName, string fileName, string displayName, string? description = null)
    {
        var gameDir = Path.Combine(_templateDirectory, gameName);
        var imagesDir = Path.Combine(gameDir, "images");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(imagesDir);

        // 复制模板图片
        var updatedRule = rule;
        if (!string.IsNullOrWhiteSpace(rule.TemplatePath))
        {
            var copiedImagePath = CopyTemplateImage(rule.TemplatePath, imagesDir, gameName);
            if (copiedImagePath != null)
            {
                // 手动创建新的 Rule 对象并复制属性
                updatedRule = new Rule
                {
                    Name = rule.Name,
                    Enabled = rule.Enabled,
                    TemplatePath = copiedImagePath,
                    MatchThreshold = rule.MatchThreshold,
                    MeanDiffThreshold = rule.MeanDiffThreshold,
                    RegionX1 = rule.RegionX1,
                    RegionY1 = rule.RegionY1,
                    RegionX2 = rule.RegionX2,
                    RegionY2 = rule.RegionY2,
                    TriggerOnce = rule.TriggerOnce,
                    Actions = rule.Actions
                };
            }
        }

        var template = new RuleScript
        {
            Schema = "autoflow/rule-script",
            FormatVersion = 1,
            Metadata = new RuleScriptMetadata
            {
                Name = displayName,
                Description = description,
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd")
            },
            Rules = new List<Rule> { updatedRule },
            IdleTickMs = 500,
            OuterWidth = 750,
            OuterHeight = 1040
        };

        var sanitizedFileName = SanitizeFileName(fileName) + ".rules.json";
        var filePath = Path.Combine(gameDir, sanitizedFileName);

        var json = JsonConvert.SerializeObject(template, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 保存多个规则为模板
    /// </summary>
    public void SaveAsTemplate(IEnumerable<Rule> rules, string templateName, string? description = null)
    {
        var template = new RuleScript
        {
            Schema = "https://autoflow.dev/rule-template.schema.json",
            FormatVersion = 1,
            Metadata = new RuleScriptMetadata
            {
                Name = templateName,
                Description = description ?? $"规则模板: {templateName}",
                Author = Environment.UserName,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            },
            Rules = new List<Rule>(rules)
        };

        var fileName = SanitizeFileName(templateName) + ".rules.json";
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

        // Recursive so templates can be organized into per-game subfolders (e.g. LastCloudia/)
        // alongside their image assets.
        var files = Directory.GetFiles(_templateDirectory, "*.rules.json", SearchOption.AllDirectories);

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
    public List<string> GetGames()
    {
        if (!Directory.Exists(_templateDirectory))
        {
            return new List<string>();
        }

        return Directory.GetDirectories(_templateDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .OrderBy(name => name)
            .ToList();
    }

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
            return $"{gameName}/images/{fileName}";
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

        // 简单比较：只比较大小，不比较内容（性能考虑）
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
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

    public string DisplayText => $"{Name} ({RuleCount} 个规则)";
}
