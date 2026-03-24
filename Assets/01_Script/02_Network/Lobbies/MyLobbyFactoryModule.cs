using MasterServerToolkit.MasterServer;
using MasterServerToolkit.Networking;
using System.Collections.Generic;
using UnityEngine;

public class MyLobbyFactoryModule : MonoBehaviour
{
    private LobbiesModule _lobbiesModule;

    protected void Awake()
    {
        _lobbiesModule = GetComponent<LobbiesModule>();
    }

    protected void Start()
    {
        _lobbiesModule.AddFactory(new GameLobbyFactory(_lobbiesModule));
    }
}


internal class GameLobbyFactory : ILobbyFactory
{
    public string Id => "Game";
    public static string DefaultName = "Game Lobby";
    private LobbiesModule _module;

    public GameLobbyFactory(LobbiesModule module)
    {
        _module = module;
    }

    public ILobby CreateLobby(MstProperties options, IPeer creator)
    {
        var properties = options.ToDictionary();

        // Create the teams
        var team = new LobbyTeam("")
        {
            MaxPlayers = int.Parse(options.AsString(Mst.Args.Names.RoomMaxConnections)),
            MinPlayers = 1
        };

        var config = new LobbyConfig();
        config.AllowJoiningWhenGameIsLive = false;
        config.PlayAgainEnabled = true; // If this is true, then the lobby state will not go into "LobbyState.GameOver", instead it will go into "LobbyState.Preparations". I'm not sure what I really should use at this time though...

        // Create the lobby
        var lobby = new BaseLobby(_module.NextLobbyId(), new LobbyTeam[] { team }, _module, config)
        {
            Name = ExtractLobbyName(properties),
        };
        lobby.SetLobbyProperties(properties);
        return lobby;
    }

    public static string ExtractLobbyName(Dictionary<string, string> properties)
    {
        return properties.ContainsKey(MstDictKeys.LOBBY_NAME) ? properties[MstDictKeys.LOBBY_NAME] : DefaultName;
    }
}