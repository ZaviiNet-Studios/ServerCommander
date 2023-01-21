﻿using System.Text;
using Microsoft.AspNetCore.Mvc;
using PlayFab;
using PlayFab.AdminModels;
using ServerCommander.Classes;
using ServerCommander.Services;
using ServerCommander.Settings.Config;

namespace ServerCommander.Controllers;

[ApiController]
[Route("[controller]")]
public class ServerController : ControllerBase
{

    private readonly ILogger<ServerController> _logger;
    private readonly MasterServerSettings _settings;

    public ServerController(ILogger<ServerController> logger)
    {
        _logger = logger;
        _settings = GameServerService.Settings;
    }

    [HttpGet("show-full-servers")]
    public ActionResult ListFull()
    {
        var enumerable = GameServerService.GetServers().Where(s => s.playerCount == s.maxCapacity)
            .Select(x => new
            {
                x.ipAddress,
                x.port,
                x.playerCount,
                x.maxCapacity
            });
        return Ok(enumerable);
    }

    [HttpGet("list-servers")]
    public ActionResult List()
    {
        var enumerable = GameServerService.GetServers().Where(s => s.playerCount == s.maxCapacity)
            .Select(x => new
            {
                x.ipAddress,
                x.port,
                x.playerCount,
                x.maxCapacity
            });
        return Ok(enumerable);
    }

    [HttpGet("connect")]
    public ActionResult Connect([FromQuery] int partySize, [FromQuery] string playfabId)
    {
        if (!_settings.AllowServerJoining)
        {
            TFConsole.WriteLine("Server joining is disabled", ConsoleColor.Yellow);
            return Ok("Server joining is disabled");
        }

        TFConsole.WriteLine(
            $"Request with party size: {partySize} {playfabId}");

        ValidateRequest(playfabId);

        // Validate token with PlayFab
        var isPlayerBanned = ValidateRequest(playfabId);

        if (!isPlayerBanned)
        {
            TFConsole.WriteLine("Player is banned", ConsoleColor.Red);
            return Ok("Player is banned");
        }

        var gameServers = GameServerService.GetServers();
        
        var availableServer = GetAvailableServer(gameServers, partySize);
        if (availableServer != null)
        {
            if (availableServer.playerCount < availableServer.maxCapacity)
            {
                if (partySize == 0)
                {
                    partySize = 1;
                }

                TFConsole.WriteLine(
                    $"Party of size {partySize} is assigned to : {availableServer.ipAddress}:{availableServer.port} InstanceID:{availableServer.instanceId} Player Count is {availableServer.playerCount}",
                    ConsoleColor.Green);

                availableServer.playerCount += partySize;
                return Ok(new
                {
                    ipAddress = availableServer.ipAddress,
                    port = availableServer.port,
                    serverId = availableServer.ServerId,
                    playerCount = availableServer.playerCount,
                });

            }
            else
            {
                return Ok("No available game servers");
            }

        }
        else
        {
            try
            {
                string serverID;
                GameServerService.CreateDockerContainer(gameServers, string.Empty, null, out string instancedID,
                    out serverID);
                GameServer newServer = GameServerService.CreateNewServer(gameServers, string.Empty, null,
                    instancedID, serverID, false);
                if (newServer != null)
                {
                    return Ok(new
                    {
                        ipAddress = newServer.ipAddress,
                        port = newServer.port,
                        playerCount = newServer.playerCount,
                        maxCapacity = newServer.maxCapacity,
                        instancedID = newServer.instanceId,
                    });
                }
                else
                {
                    throw new Exception();
                }
            }
            catch
            {
                return Ok("Error creating new server");
            }
        }
    }

    private static GameServer? GetAvailableServer(List<GameServer> gameServers, int partySize)
    {
        // Check if there are any servers in the list
        if (gameServers.Count == 0)
        {
            // If no servers, return null
            return null;
        }

        // Sort the list of game servers by player count
        gameServers.Sort((a, b) => a.playerCount.CompareTo(b.playerCount));

        // Find the first game server with a player count less than its maximum capacity
        var availableServer =
            gameServers.FirstOrDefault(server => server.playerCount + partySize <= server.maxCapacity);
        // Return the available server
        // If no available servers, return the server with the lowest player count
        return availableServer ?? gameServers[0];
    }


    private bool ValidateRequest(string playfabID)
    {
        if (!_settings.UsePlayFab) return true;
        var adminAPISettings = new PlayFabApiSettings()
        {
            TitleId = _settings.PlayFabTitleID,
            DeveloperSecretKey = _settings.DeveloperSecretKey
        };

        var authenticationApi = new PlayFabAdminInstanceAPI(adminAPISettings);


        TFConsole.WriteLine("Validating Player " + playfabID);


        var request = new GetUserBansRequest()
        {
            PlayFabId = playfabID
        };

        Task<PlayFabResult<GetUserBansResult>> task = authenticationApi.GetUserBansAsync(request);
        task.Wait();

        var response = task.Result;


        var isBanned = response.Result.BanData.Count;

        TFConsole.WriteLine($"Player has {isBanned} Ban(s) on Record");

        if (isBanned > 0)
        {
            return false;
        }

        return true;

    }
}