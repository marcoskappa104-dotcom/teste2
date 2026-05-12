using UnityEngine;
using UnityEngine.UI;

namespace RPG.UI
{
    /// <summary>
    /// Barra de HP que flutua sobre o monstro e olha sempre para a câmera.
    /// Aparece apenas quando o HP é menor que o máximo (não polui a tela).
    /// </summary>
    public class MonsterHealthBarUI : MonoBehaviour
    {
        [SerializeField] private Slider     hpSlider;
        [SerializeField] private GameObject container;

        private Camera _cam;

        private void Start()
        {
            _cam = Camera.main;
            if (container != null) container.SetActive(false);
        }

        private void LateUpdate()
        {
            // Recacheia se a câmera foi destruída (ex: troca de cena)
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            transform.forward = _cam.transform.forward;
        }

        public void UpdateBar(float current, float max)
        {
            if (hpSlider == null) return;
            hpSlider.maxValue = max;
            hpSlider.value    = current;

            if (container != null)
                container.SetActive(current < max);
        }
    }
}
