using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;
using System.Runtime.InteropServices;

namespace FidgetSpinner;

public sealed class Plugin : IDalamudPlugin
{
    [StructLayout(LayoutKind.Explicit, Size = 0x140)]
    public struct MoveControllerSubMemberForMine
    {
        // Only set for Standard control scheme.
        [FieldOffset(0x121)] public byte RightClickCameraAiming;
    }

    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooker { get; private set; } = null!;

    private unsafe delegate bool RMIWalkIsInputEnabled(void* self);
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled1;
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled2;
    private unsafe delegate float RMIWalkTurnSpeed(MoveControllerSubMemberForMine* self, void* unused, byte* isTurningOverride);
    [Signature("E8 ?? ?? ?? ?? 48 8B 47 20 0F 28 C8", DetourName = nameof(TurnSpeedDetour))]
    private readonly Hook<RMIWalkTurnSpeed> turnSpeedHook;
    private unsafe delegate void RMIWalkDelegate(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour))]
    private readonly Hook<RMIWalkDelegate> rmiWalkHook;

    private const float TargetTurnSpeed = 1.5f * 1.57f;
    private const float MaxSpeedMultiplier = 10f;

    private const string CommandName = "/fspin";

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "`/fspin left` or `/fspin right`.\nOptionally include a decimal multiplier (0, 10] to adjust spin speed like `/fspin left 0.5` for half speed or `/fspin right 3` for triple speed."
        });
        var rmiWalkIsInputEnabled1Addr = SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C");
        var rmiWalkIsInputEnabled2Addr = SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F");
        _rmiWalkIsInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled1Addr);
        _rmiWalkIsInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled2Addr);
        Hooker.InitializeFromAttributes(this);
    }

    public void Dispose()
    {
        rmiWalkHook?.Dispose();
        turnSpeedHook?.Dispose();
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
                turnOverride *= clamp(multiplier);
            }
        }
        if (turnOverride == 0)
        {
            turnOverride = null;
        }
        if (turnOverride != null)
        {
            rmiWalkHook?.Enable();
        }
    }

    float? turnOverride = null;

    private unsafe void RMIWalkDetour(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);
        if (*sumTurnLeft != 0 || *sumLeft != 0 || *sumForward != 0 || self->RightClickCameraAiming != 0)
        {
            clearTurnOverride();
        }

        var movementAllowed = bAdditiveUnk == 0 && _rmiWalkIsInputEnabled1(self) && _rmiWalkIsInputEnabled2(self);
        if (turnOverride != null && movementAllowed)
        {
            turnSpeedHook.Enable();
            *sumTurnLeft = MathF.Sign(turnOverride.Value);
        }
        else
        {
            turnSpeedHook.Disable();
        }
    }

    private unsafe float TurnSpeedDetour(MoveControllerSubMemberForMine* self, void* unused, byte* isTurningOverride)
    {
        if (turnOverride != null)
        {
            return TargetTurnSpeed * MathF.Abs(turnOverride.Value);
        }
        return turnSpeedHook.Original(self, unused, isTurningOverride);
    }

    private void clearTurnOverride()
    {
        rmiWalkHook?.Disable();
        turnSpeedHook?.Disable();
        turnOverride = null;
    }

    private float clamp(float value)
    {
        return Math.Clamp(value, -MaxSpeedMultiplier, MaxSpeedMultiplier);
    }
}
