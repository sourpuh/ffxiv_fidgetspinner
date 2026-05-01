using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;

namespace FidgetSpinner;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooker { get; private set; } = null!;

    private unsafe delegate void RMIWalkDelegate(nint self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour))]
    private readonly Hook<RMIWalkDelegate> rmiWalkHook;

    private const string CommandName = "/fspin";

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "`/fspin left` or `/fspin right`"
        });
        Hooker.InitializeFromAttributes(this);
        rmiWalkHook?.Enable();
    }

    public void Dispose()
    {
        rmiWalkHook?.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        if (args == "left")
        {
            turnOverride = 0.5f;
        }
        else if (args == "right")
        {
            turnOverride = -1;
        }
        else
        {
            turnOverride = null;
        }
    }

    float? turnOverride = null;

    private unsafe void RMIWalkDetour(nint self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);
        if (*sumTurnLeft != 0 || *sumLeft != 0 || *sumForward != 0)
        {
            turnOverride = null;
        }
        if (turnOverride != null)
        {
            *sumTurnLeft = turnOverride.Value;
        }
    }
}
