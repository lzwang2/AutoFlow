using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Registry for instruction handlers, keyed by CommandType
/// </summary>
public sealed class InstructionHandlerRegistry
{
    private readonly Dictionary<CommandType, IInstructionHandler> _handlers = new();

    public void Register(IInstructionHandler handler)
    {
        _handlers[handler.CommandType] = handler;
    }

    public IInstructionHandler? GetHandler(CommandType commandType)
    {
        return _handlers.GetValueOrDefault(commandType);
    }

    public bool HasHandler(CommandType commandType)
    {
        return _handlers.ContainsKey(commandType);
    }

    /// <summary>
    /// Create a registry with all standard handlers registered
    /// </summary>
    public static InstructionHandlerRegistry CreateDefault()
    {
        var registry = new InstructionHandlerRegistry();

        registry.Register(new CommentHandler());
        registry.Register(new WaitTimeHandler());
        registry.Register(new ClickHandler());
        registry.Register(new WaitImageHandler());
        registry.Register(new WaitColorHandler());
        registry.Register(new LoopHandler());
        registry.Register(new CallSkillHandler());

        return registry;
    }
}
