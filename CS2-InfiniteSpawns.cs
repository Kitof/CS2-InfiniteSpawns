using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using Serilog.Core;

using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace InfiniteSpawns;

public class InfiniteSpawns : BasePlugin
{
    public override string ModuleName => "CS2-InfiniteSpawns";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Kitof";
    public override string ModuleDescription => "Infinite spawns";
    public Random Random = new Random();


    public enum JoinTeamReason
    {
        TeamsFull = 1,
        TerroristTeamFull = 2,
        CTTeamFull = 3,
        TTeamLimit = 7,
        CTTeamLimit = 8
    }
    public int TerroristSpawns = -1;
    public int CTSpawns = -1;
    public bool respectlimitteams = true;
    private Dictionary<CCSPlayerController, int> SelectedTeam = new Dictionary<CCSPlayerController, int>();
    public class CustomSpawnPoint
    {
        public CsTeam Team { get; set; }
        public required string Origin { get; set; }
        public required string Angle { get; set; }
    }
    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        AddCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        AddCommandListener("bot_add", OnBotAddCommand, HookMode.Pre);
        RegisterEventHandler<EventJointeamFailed>(OnTeamJoinFailed, HookMode.Pre);
        RegisterEventHandler<EventPlayerSpawn>(Event_PlayerSpawn, HookMode.Post);
    }
    public HookResult OnBotAddCommand(CCSPlayerController? player, CommandInfo info)
    {
        UpdateSpawnsNumbers();
        if (info.ArgCount > 0 && info.ArgByIndex(0).ToLower() == "bot_add")
        {
            int iTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist).Count();
            int iCTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.CounterTerrorist).Count();
            if (iTs == TerroristSpawns)
            {
                NewSpawnFromDefault("t");
            }
            if (iCTs == CTSpawns)
            {
                NewSpawnFromDefault("ct");
            }
        }
        return HookResult.Continue;
    }
    public HookResult OnJoinTeamCommand(CCSPlayerController? player, CommandInfo info)
    {
        UpdateSpawnsNumbers();
        if (player != null && player.IsValid && info.ArgCount > 0 && info.ArgByIndex(0).ToLower() == "jointeam")
        {
            if (info.ArgCount > 1)
            {
                string teamArg = info.ArgByIndex(1);
                if (int.TryParse(teamArg, out int teamId))
                {
                    if (teamId >= (int)CsTeam.Spectator && teamId <= (int)CsTeam.CounterTerrorist)
                    {
                        SelectedTeam[player] = teamId;
                    }
                }
                else
                {
                    Console.WriteLine("Failed to parse team ID.");
                }
            }
        }
        return HookResult.Continue;
    }
    public HookResult OnTeamJoinFailed(EventJointeamFailed @event, GameEventInfo info)
    {
        Logger.LogInformation("[InfiniteSpawns] OnTeamJoinFailed");
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        JoinTeamReason m_eReason = (JoinTeamReason)@event.Reason;
        int iTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist).Count();
        int iCTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.CounterTerrorist).Count();

        UpdateSpawnsNumbers();

        if (!SelectedTeam.ContainsKey(player))
        {
            SelectedTeam[player] = 0;
        }

        switch (m_eReason)
        {
            case JoinTeamReason.TeamsFull:

                if (iCTs >= CTSpawns && iTs >= TerroristSpawns)
                {
                    NewSpawnFromDefault("t");
                    NewSpawnFromDefault("ct");
                    return HookResult.Continue;
                }

                break;

            case JoinTeamReason.TerroristTeamFull:
                if (iTs >= TerroristSpawns)
                {
                    return NewSpawnFromDefault("t") ? HookResult.Continue : HookResult.Stop;
                }

                break;

            case JoinTeamReason.CTTeamFull:
                if (iCTs >= CTSpawns)
                {
                    return NewSpawnFromDefault("ct") ? HookResult.Continue : HookResult.Stop;
                }

                break;

            default:
                {
                    return HookResult.Continue;
                }
        }

        return HookResult.Continue;
    }

    public void UpdateSpawnsNumbers()
    {
        TerroristSpawns = 0;
        CTSpawns = 0;

        var tSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
        var ctSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");

        foreach (var spawn in tSpawns)
        {
            TerroristSpawns++;
        }

        foreach (var spawn in ctSpawns)
        {
            CTSpawns++;
        }

        Logger.LogInformation($"TerroristSpawns={TerroristSpawns}, CTSpawns={CTSpawns}");
    }
    public void OnMapStart(string mapname)
    {
        Logger.LogInformation("[FTF] OnMapStart");
        AddTimer(1.0f, () =>
        {
            try
            {
                UpdateSpawnsNumbers();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[FTF] Exception inside OnMapStart timer.");
            }
        });
    }

    public bool NewSpawnFromDefault(string team)
    {
        var ctspawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").ToList();
        var tspawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").ToList();
        if (team == "ct" && ctspawns.Count == 0)
        {
            Logger.LogError("This map contains no CT spawns.");
            return false;
        }
        if (team == "t" && tspawns.Count == 0)
        {
            Logger.LogError("This map contains no T spawns.");
            return false;
        }
        var spawn = (team == "ct") ? ctspawns[Random.Next(ctspawns.Count)] : tspawns[Random.Next(tspawns.Count)];
        var origin = spawn.AbsOrigin;
        var angle = spawn.AbsRotation;
        if (origin == null || angle == null)
        {
            Logger.LogError("Origin or angle not found when attempting to create a spawn.");
            return false;
        }

        var point = new CustomSpawnPoint
        {
            Team = team == "ct" ? CsTeam.CounterTerrorist : CsTeam.Terrorist,
            Origin = VectorToString(new Vector3(origin.X, origin.Y, origin.Z)),
            Angle = VectorToString(new Vector3(angle.X, angle.Y, angle.Z))
        };

        if (CreateEntity(point))
        {
            if (team == "ct") CTSpawns++;
            else TerroristSpawns++;

            Logger.LogInformation($"New {team} spawn created.");
            return true;
        }
        else
        {
            Logger.LogInformation("Spawn creation failed.");
            return false;
        }
    }

    public bool CreateEntity(CustomSpawnPoint spawnPoint)
    {
        var noVel = new Vector(0f, 0f, 0f);
        SpawnPoint? entity;
        if (spawnPoint.Team == CsTeam.Terrorist)
        {
            entity = Utilities.CreateEntityByName<CInfoPlayerTerrorist>("info_player_terrorist");
        }
        else
        {
            entity = Utilities.CreateEntityByName<CInfoPlayerCounterterrorist>("info_player_counterterrorist");
        }
        if (entity == null)
        {
            return false;
        }
        var angle = StringToVector(spawnPoint.Angle);
        entity.Teleport(NormalVectorToValve(StringToVector(spawnPoint.Origin)), new QAngle(angle.X, angle.Y, angle.Z), noVel);
        entity.DispatchSpawn();
        return true;
    }

    private readonly Dictionary<CCSPlayerController, CounterStrikeSharp.API.Modules.Timers.Timer?> _collisionTimers = new();

    private HookResult Event_PlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid)
        {
            return HookResult.Continue;
        }

        CCSPlayerController player = @event.Userid;

        if (player.Connected != PlayerConnectedState.Connected || !player.PlayerPawn.IsValid)
        {
            return HookResult.Continue;
        }

        CHandle<CCSPlayerPawn> pawn = player.PlayerPawn;

        Server.NextFrame(() => PlayerSpawnNextFrame(player, pawn));

        return HookResult.Continue;
    }

    private void PlayerSpawnNextFrame(CCSPlayerController player, CHandle<CCSPlayerPawn> pawn)
    {
        var currentPawn = pawn.Value;
        if (currentPawn == null || !currentPawn.IsValid)
            return;

        // Kill any existing collision timer for this player
        if (_collisionTimers.TryGetValue(player, out var existingTimer) && existingTimer != null)
        {
            existingTimer.Kill();
            _collisionTimers.Remove(player);
        }

        currentPawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        currentPawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        Utilities.SetStateChanged(currentPawn, "CCollisionProperty", "m_collisionAttribute");

        // Start the distance-check sequence
        StartCollisionCheckSequence(player, pawn);
    }

    private void StartCollisionCheckSequence(CCSPlayerController player, CHandle<CCSPlayerPawn> pawn)
    {
        var timer = AddTimer(1.0f, () =>
        {
            var currentPawn = pawn.Value;
            if (currentPawn == null || !currentPawn.IsValid || currentPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                // Player dead/disconnected/invalid - stop sequence
                if (_collisionTimers.TryGetValue(player, out var t) && t != null)
                {
                    t.Kill();
                    _collisionTimers.Remove(player);
                }
                return;
            }

            // Check distance to all other valid players
            bool canRestore = true;
            foreach (var other in Utilities.GetPlayers())
            {
                if (other == player || !other.IsValid || other.PlayerPawn == null || !other.PlayerPawn.IsValid)
                    continue;

                var otherPawn = other.PlayerPawn.Value;
                if (otherPawn == null || !otherPawn.IsValid || otherPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    continue;

                var pos1 = currentPawn.AbsOrigin;
                var pos2 = otherPawn.AbsOrigin;
                if (pos1 == null || pos2 == null)
                    continue;

                float dx = pos1.X - pos2.X;
                float dy = pos1.Y - pos2.Y;
                float dz = pos1.Z - pos2.Z;
                float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                if (distance <= 32.0f)
                {
                    canRestore = false;
                    break;
                }
            }

            if (canRestore)
            {
                // Restore collision
                currentPawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
                currentPawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
                Utilities.SetStateChanged(currentPawn, "CCollisionProperty", "m_collisionAttribute");

                // Clean up timer
                if (_collisionTimers.TryGetValue(player, out var t2) && t2 != null)
                {
                    t2.Kill();
                    _collisionTimers.Remove(player);
                }
            }
            // If not canRestore, timer will fire again in 1 second (REPEAT)
        }, TimerFlags.REPEAT);

        _collisionTimers[player] = timer;
    }

    private static string VectorToString(Vector3 vec)
    {
        return $"{vec.X}|{vec.Y}|{vec.Z}";
    }

    private static Vector3 StringToVector(string str)
    {
        var explode = str.Split("|");
        return new Vector3(x: float.Parse(explode[0]), y: float.Parse(explode[1]), z: float.Parse(explode[2]));
    }

    private static Vector NormalVectorToValve(Vector3 v)
    {
        return new Vector(v.X, v.Y, v.Z);
    }
    public static int GetTeamPlayerCount(CsTeam team)
    {
        return Utilities.GetPlayers().Count(p => p.Team == team);
    }
}

internal static class IsValid
{
    public static bool PlayerIndex(uint playerIndex)
    {
        if(playerIndex == 0)
        {
            return false;
        }

        if(!(1 <= playerIndex && playerIndex <= Server.MaxPlayers))
        {
            return false;
        }

        return true;
    }
}
