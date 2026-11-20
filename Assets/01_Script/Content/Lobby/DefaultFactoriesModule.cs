using MasterServerToolkit.MasterServer;
using MasterServerToolkit.Networking;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DefaultLobbyFactory : ILobbyFactory
{
    public string Id => "Default";
    public static string DefaultName = "Default Lobby";
    private LobbiesModule _module;

    public DefaultLobbyFactory(LobbiesModule module)
    {
        _module = module;
    }

    public ILobby CreateLobby(MstProperties options, IPeer creator)
    {
        var properties = options.ToDictionary();

        // Create the teams
        var team = new LobbyTeam("")
        {
            MaxPlayers = 10,
            MinPlayers = 1
        };

        var config = new LobbyConfig();
        config.PlayAgainEnabled = true; // If this is true, then the lobby state will not go into "LobbyState.GameOver", instead it will go into "LobbyState.Preparations". I'm not sure what I really should use at this time though...

        // Create the lobby
        var lobby = new BaseLobby(_module.NextLobbyId(),
            new[] { team }, _module, config)
        {
            Name = ExtractLobbyName(properties)
        };

        // Override properties with what user provided
        lobby.SetLobbyProperties(properties);

        // Add control for the game speed
        lobby.AddControl(new LobbyPropertyData()
        {
            Label = "Game Speed",
            Options = new List<string>() { "1x", "2x", "3x" },
            PropertyKey = "speed"
        }, "2x"); // Default option

        return lobby;
    }

    public static string ExtractLobbyName(Dictionary<string, string> properties)
    {
        return properties.ContainsKey(MstDictKeys.LOBBY_NAME) ? properties[MstDictKeys.LOBBY_NAME] : DefaultName;
    }
}
