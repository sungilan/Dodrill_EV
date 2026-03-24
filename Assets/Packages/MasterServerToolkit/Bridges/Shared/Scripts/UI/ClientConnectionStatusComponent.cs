using MasterServerToolkit.MasterServer;
using MasterServerToolkit.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MasterServerToolkit.Bridges
{
    public class ClientConnectionStatusComponent : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private Image connectionBgImage;
        [SerializeField] private TextMeshProUGUI connectionText;
        [SerializeField] private Image authBgImage;
        [SerializeField] private TextMeshProUGUI authText;

        [Header("Status Backgrounds")]
        [SerializeField] private Sprite connectingSprite;  // 주황색
        [SerializeField] private Sprite offlineSprite;     // 빨간색
        [SerializeField] private Sprite onlineSprite;      // 파란색
        [SerializeField] private Sprite unknownSprite;     // 진파란색

        public IClientSocket Connection => Mst.Connection;

        protected virtual void Start()
        {
            Connection.OnStatusChangedEvent += OnStatusChangedEventHandler;
            OnStatusChangedEventHandler(Connection.Status);
        }

        protected virtual void OnStatusChangedEventHandler(ConnectionStatus status)
        {
            string address = $"{Connection.Address}:{Connection.Port}";

            switch (status)
            {
                case ConnectionStatus.Connected:
                    RepaintConnection($"Connected to\n{address}", onlineSprite);
                    break;
                case ConnectionStatus.Disconnected:
                    RepaintConnection("Disconnected", offlineSprite);
                    break;
                case ConnectionStatus.Connecting:
                    RepaintConnection($"Connecting to\n{address}", connectingSprite);
                    break;
                default:
                    RepaintConnection("Unknown status", unknownSprite);
                    break;
            }
        }

        public void SetAuthStatus(bool isAuthorized, string userName = "")
        {
            if (isAuthorized)
                RepaintAuth(userName, onlineSprite);
            else
                RepaintAuth("Not authorized", offlineSprite);
        }

        private void RepaintConnection(string msg, Sprite sprite)
        {
            if (connectionBgImage != null) connectionBgImage.sprite = sprite;
            if (connectionText != null) connectionText.text = msg;
        }

        private void RepaintAuth(string msg, Sprite sprite)
        {
            if (authBgImage != null) authBgImage.sprite = sprite;
            if (authText != null) authText.text = msg;
        }

        protected virtual void OnDestroy()
        {
            if (Connection != null)
                Connection.OnStatusChangedEvent -= OnStatusChangedEventHandler;
        }
    }
}
