using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;

namespace VipPlugin;

public class VipConfig : BasePluginConfig
{
    [JsonPropertyName("VipFlag")]
    public string VipFlag { get; set; } = "@css/vip";

    [JsonPropertyName("HpRegenAmount")]
    public int HpRegenAmount { get; set; } = 2;

    [JsonPropertyName("HpRegenInterval")]
    public float HpRegenInterval { get; set; } = 20.0f;

    [JsonPropertyName("SmokeColor")]
    public string SmokeColor { get; set; } = "0 255 0"; // Green by default (R G B)

    [JsonPropertyName("EnableReservedSlot")]
    public bool EnableReservedSlot { get; set; } = true;
}

[MinimumApiVersion(80)]
public class VipPlugin : BasePlugin, IPluginConfig<VipConfig>
{
    public override string ModuleName => "Simple VIP Features";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "CSSharp Developer";

    public VipConfig Config { get; set; } = new();
    private Vector? _parsedSmokeColor;

    public void OnConfigParsed(VipConfig config)
    {
        Config = config;
        _parsedSmokeColor = ParseColor(Config.SmokeColor);
    }

    public override void Load(bool hotReload)
    {
        // 1. HP Regeneration Timer
        AddTimer(Config.HpRegenInterval, OnRegenTimer, TimerFlags.REPEAT);

        // 2. Smoke Color Logic
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);

        // 3. Reserved Slot Logic
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
    }

    private void OnRegenTimer()
    {
        var players = Utilities.GetPlayers();

        foreach (var player in players)
        {
            if (IsValidPlayer(player) && IsVip(player) && player.PawnIsAlive)
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn == null) continue;

                // Don't heal if already at 100 (or max)
                if (pawn.Health >= 100) continue;

                int newHealth = pawn.Health + Config.HpRegenAmount;
                if (newHealth > 100) newHealth = 100;

                pawn.Health = newHealth;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                
                // Optional: Small sound or message could go here
            }
        }
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (entity.DesignerName != "smokegrenade_projectile") return;

        var smokeProjectile = new CSmokeGrenadeProjectile(entity.Handle);

        // We wait one frame to ensure the Owner (Thrower) is linked to the projectile
        Server.NextFrame(() =>
        {
            if (smokeProjectile == null || !smokeProjectile.IsValid) return;

            // Get the thrower
            var throwerValue = smokeProjectile.Thrower.Value;
            if (throwerValue == null) return;

            var throwerController = throwerValue.Controller.Value?.As<CCSPlayerController>();

            if (IsValidPlayer(throwerController) && IsVip(throwerController))
            {
                if (_parsedSmokeColor != null)
                {
                    // Set the smoke color property
                    smokeProjectile.SmokeColor = _parsedSmokeColor;
                    Utilities.SetStateChanged(smokeProjectile, "CSmokeGrenadeProjectile", "m_vSmokeColor");
                }
            }
        });
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (!Config.EnableReservedSlot) return HookResult.Continue;

        var connectingPlayer = @event.Userid;
        if (connectingPlayer == null || !connectingPlayer.IsValid) return HookResult.Continue;

        // Only run logic if the connecting player is a VIP
        if (IsVip(connectingPlayer))
        {
            var players = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected).ToList();
            
            // Check if server is full (MaxPlayers is usually the visible limit)
            if (players.Count >= Server.MaxPlayers)
            {
                // Find a target to kick: Not a VIP, and preferably not a Bot (unless only bots exist)
                // We sort by connection time to kick the newest player, or random. Here we kick the newest non-VIP.
                var targetToKick = players
                    .Where(p => !IsVip(p) && !p.IsBot && p.UserId != connectingPlayer.UserId)
                    .OrderByDescending(p => p.ConnectedTime) // Kick newest player
                    .FirstOrDefault();

                // If no real players to kick, try kicking a bot
                if (targetToKick == null)
                {
                    targetToKick = players.FirstOrDefault(p => p.IsBot);
                }

                if (targetToKick != null)
                {
                    Server.PrintToConsole($"[VIP] Kicking {targetToKick.PlayerName} to make room for VIP {connectingPlayer.PlayerName}.");
                    Server.ExecuteCommand($"kickid {targetToKick.UserId} \"Kicked to make room for a VIP\"");
                }
            }
        }

        return HookResult.Continue;
    }

    // --- Helpers ---

    private bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && !player.IsBot && player.Connected == PlayerConnectedState.PlayerConnected;
    }

    private bool IsVip(CCSPlayerController player)
    {
        // Check for the specific flag defined in config (default @css/vip)
        return AdminManager.PlayerHasPermissions(player, Config.VipFlag);
    }

    private Vector? ParseColor(string colorString)
    {
        try
        {
            var parts = colorString.Split(' ');
            if (parts.Length == 3)
            {
                float r = float.Parse(parts[0]);
                float g = float.Parse(parts[1]);
                float b = float.Parse(parts[2]);
                return new Vector(r, g, b);
            }
        }
        catch
        {
            Console.WriteLine($"[VipPlugin] Error parsing smoke color: {colorString}. Using default.");
        }
        return null;
    }
}