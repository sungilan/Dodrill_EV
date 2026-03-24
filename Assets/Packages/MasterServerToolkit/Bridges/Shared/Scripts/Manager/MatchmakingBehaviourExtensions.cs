using MasterServerToolkit.MasterServer;
using UnityEngine.Events;

namespace MasterServerToolkit.Bridges
{
    public static class MatchmakingBehaviourExtensions
    {
        /// <summary>
        /// Sends request to master server to start new lobby
        /// </summary>
        /// <param name="spawnOptions"></param>
        public static void CreateNewLobby(this MatchmakingBehaviour mmb, string factory, MstProperties options, UnityAction failCallback = null, string platformType = "")
        {
            Mst.Events.Invoke(MstEventKeys.showLoadingInfo, $"Starting {factory} lobby... Please wait!");

            var logger = Mst.Create.Logger(nameof(MatchmakingBehaviourExtensions));
            logger.Debug($"Starting {factory} lobby... Please wait!");

            options.Add(Mst.Args.Names.StartClientConnection, true);

            Mst.Client.Lobbies.CreateAndJoin(factory, options, (lobby, error) =>
            {
                if(!string.IsNullOrWhiteSpace(error))
                {
                    Mst.Events.Invoke(MstEventKeys.showOkDialogBox, new OkDialogBoxEventMessage($"Create New Lobby failed: {error}", () =>
                    {
                        failCallback?.Invoke();
                    }));

                    return;
                }
                else
                {

                    Mst.Events.Invoke(MstEventKeys.showLobbyView, lobby);

                    return;
                }
            }, platformType);
        }

        public static void JoinLobby(this MatchmakingBehaviour mmb, GameInfoPacket gameInfo, string platformType = "")
        {
            var options = new MstProperties();
            options.Add(Mst.Args.Names.LobbyId, gameInfo.Id); // I'm not sure if this is correct. -Jason

            Mst.Client.Lobbies.JoinLobby(gameInfo.Id, (lobby, error) =>
            {
                if(!string.IsNullOrWhiteSpace(error))
                {
                    Mst.Events.Invoke(MstEventKeys.showLoadingInfo, $"Join lobby error: [{error}]");
                }
                else
                {
                    Mst.Events.Invoke(MstEventKeys.showLobbyView, lobby);
                    return;
                }
            }, platformType);
        }
    }

}