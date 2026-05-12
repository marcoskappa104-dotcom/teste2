using UnityEngine;
using Mirror;
using RPG.UI;
using RPG.Character;

namespace RPG.Network
{
    /// <summary>
    /// Conecta o UIManager ao PlayerEntity local quando ele chega.
    /// Em multiplayer o player spawna depois da UI existir, então fazemos
    /// retry leve até a conexão.
    /// </summary>
    public class NetworkUIConnector : MonoBehaviour
    {
        private const float RETRY_INTERVAL = 0.5f;

        private bool  _connected;
        private float _retryTimer;

        private void Start() => TryConnect();

        private void Update()
        {
            if (_connected) return;

            _retryTimer += Time.deltaTime;
            if (_retryTimer >= RETRY_INTERVAL)
            {
                _retryTimer = 0f;
                TryConnect();
            }
        }

        private void TryConnect()
        {
            if (_connected) return;
            if (!NetworkClient.active) return;
            if (NetworkClient.localPlayer == null) return;

            var playerEntity = NetworkClient.localPlayer.GetComponent<PlayerEntity>();
            if (playerEntity == null) return;

            if (playerEntity.IsInitialized)
            {
                BindUI(playerEntity);
            }
            else
            {
                // Evita múltiplos binds caso entre aqui várias vezes
                playerEntity.OnInitialized -= OnPlayerInitialized;
                playerEntity.OnInitialized += OnPlayerInitialized;
            }
        }

        private void OnPlayerInitialized()
        {
            if (_connected) return;

            var playerEntity = NetworkClient.localPlayer?.GetComponent<PlayerEntity>();
            if (playerEntity != null)
                BindUI(playerEntity);
        }

        private void BindUI(PlayerEntity playerEntity)
        {
            if (_connected) return;
            _connected = true;

            UIManager.Instance?.BindLocalPlayer(playerEntity);
            Debug.Log("[NetworkUIConnector] HUD conectado ao player local.");
        }
    }
}
