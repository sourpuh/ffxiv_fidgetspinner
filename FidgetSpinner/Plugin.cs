using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FidgetSpinner;

public sealed class Plugin : IDalamudPlugin
{
    [StructLayout(LayoutKind.Explicit, Size = 0x140)]
    public struct MoveControllerSubMemberForMine
    {
        [FieldOffset(0x121)] public byte RightClickCameraAiming;
    }

    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooker { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    private Stopwatch spinTimer = new();

    private unsafe delegate bool RMIWalkIsInputEnabled(void* self);
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled1;
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled2;
    private unsafe delegate void RMIWalkDelegate(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour))]
    private readonly Hook<RMIWalkDelegate> rmiWalkHook;

    private const string CommandName = "/fspin";

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "`/fspin left` or `/fspin right`"
        });
        var rmiWalkIsInputEnabled1Addr = SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C");
        var rmiWalkIsInputEnabled2Addr = SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F");
        _rmiWalkIsInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled1Addr);
        _rmiWalkIsInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled2Addr);
        Hooker.InitializeFromAttributes(this);
        rmiWalkHook?.Enable();
    }

    public void Dispose()
    {
        rmiWalkHook?.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string argstr)
    {
        clearTurnOverride();

        var args = argstr.Split(' ');
        if (args.Length > 0)
        {
            if (args[0] == "left")
            {
                turnOverride = 1;
            }
            else if (args[0] == "right")
            {
                turnOverride = -1;
            }
        }
        if (args.Length == 2)
        {
            if (float.TryParse(args[1], out var multiplier))
            {
                // Clamp [-1, 1] because the game doesn't let you go over that.
                turnOverride *= MathF.Max(MathF.Min(multiplier, 1), -1);
            }
        }
    }

    float prevRotation = 0;
    float? turnOverride = null;

    private unsafe void RMIWalkDetour(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);
        if (*sumTurnLeft != 0 || *sumLeft != 0 || *sumForward != 0 || self->RightClickCameraAiming != 0)
        {
            clearTurnOverride();
        }
        var player = ObjectTable.LocalPlayer;
        if (turnOverride != null && player != null)
        {
            if (player.Rotation < 0 && prevRotation > 0)
            {
                // At normal spin speed, a full rotation should take about 2.66 seconds.
                // If the nonu is spinning faster than 2.5s, activate the kill switch because something is broken.
                if (spinTimer.IsRunning && spinTimer.Elapsed.TotalSeconds < 2.5)
                {
                    Chat.PrintError($"Fidget Spinning too fast ({spinTimer.Elapsed.TotalSeconds}s)! Kill switch activated! Try restarting the plugin?");
                    clearTurnOverride();
                }
                spinTimer.Restart();
            }
            prevRotation = player.Rotation;
        }

        var movementAllowed = bAdditiveUnk == 0 && _rmiWalkIsInputEnabled1(self) && _rmiWalkIsInputEnabled2(self);
        if (turnOverride != null && movementAllowed)
        {
            *sumTurnLeft = turnOverride.Value;
        }
    }

    private void clearTurnOverride()
    {
        spinTimer.Stop();
        turnOverride = null;
    }
}
