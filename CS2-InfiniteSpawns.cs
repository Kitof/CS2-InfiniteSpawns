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

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using Serilog.Core;

using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace InfiniteSpawns;

public class InfiniteSpawns : BasePlugin
{
    public override string ModuleName => "CS2-InfiniteSpawns";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Kitof";
    public override string ModuleDescription => "Infinite spawns";
    public Random Random = new Random();
    // Sets the correct offset data for our collision rules change in accordance with the server's operating system
    private readonly WIN_LINUX<int> OnCollisionRulesChangedOffset = new WIN_LINUX<int>(173, 172);

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

    private HookResult Event_PlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!@event.Userid.IsValid)
        {
            return HookResult.Continue;
        }
        CCSPlayerController player = @event.Userid;

        if (player.Connected != PlayerConnectedState.PlayerConnected)
        {
            return HookResult.Continue;
        }

        if (!player.PlayerPawn.IsValid)
        {
            return HookResult.Continue;
        }

        CHandle<CCSPlayerPawn> pawn = player.PlayerPawn;

        Server.NextFrame(() => PlayerSpawnNextFrame(player, pawn));

        return HookResult.Continue;
    }

    private void PlayerSpawnNextFrame(CCSPlayerController player, CHandle<CCSPlayerPawn> pawn)
    {
        pawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        pawn.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        VirtualFunctionVoid<nint> collisionRulesChanged = new VirtualFunctionVoid<nint>(pawn.Value.Handle, OnCollisionRulesChangedOffset.Get());
        collisionRulesChanged.Invoke(pawn.Value.Handle);

        AddTimer(3.0f, () =>
        {
            Server.PrintToConsole("End of timer");

            pawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
            pawn.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
            VirtualFunctionVoid<nint> collisionRulesChanged = new VirtualFunctionVoid<nint>(pawn.Value.Handle, OnCollisionRulesChangedOffset.Get());
            collisionRulesChanged.Invoke(pawn.Value.Handle);
        });

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



// NOTE:
//      This class was made by KillStr3aK / Bober repository and allows you to perform actions depending on whether the server is running a linux or windows operating system
//      - https://github.com/KillStr3aK/CSSharpTests/blob/master/Models/WIN_LINUX.cs
public class WIN_LINUX<T>
{
    [JsonPropertyName("Windows")]
    public T Windows { get; private set; }

    [JsonPropertyName("Linux")]
    public T Linux { get; private set; }

    public WIN_LINUX(T windows, T linux)
    {
        this.Windows = windows;
        this.Linux = linux;
    }

    public T Get()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return this.Windows;
        }
        else
        {
            return this.Linux;
        }
    }
}
