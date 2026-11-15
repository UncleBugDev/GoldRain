using UnityEngine;

namespace GoldRain.Core
{
    /// <summary>
    /// Компонент, накладываемый на персонажа при попадании на него мочи.
    /// Вызывает эффект промокания (Wet) и генерирует золотистые стекающие капли вместо синей воды.
    /// </summary>
    public class GoldPeeWetDrip : MonoBehaviour
    {
        private Player? _player;
        private float _remainingDuration;
        private GameObject? _dripEmitterObject;
        private ParticleSystem? _dripParticleSystem;
        private ParticleSystemRenderer? _dripRenderer;

        private static Material? s_dripMaterial;

        public static void Apply(Player player, float duration = 15f)
        {
            if (player == null || player.IsDead()) return;

            // 1. Накладываем ванильный статус-эффект намокания (Wet)
            var seman = player.GetSEMan();
            if (seman != null)
            {
                seman.AddStatusEffect("Wet".GetStableHashCode(), true);
            }

            // 2. Добавляем или обновляем компонент золотых капель
            var comp = player.GetComponent<GoldPeeWetDrip>();
            if (comp == null)
            {
                comp = player.gameObject.AddComponent<GoldPeeWetDrip>();
            }
            comp.Refresh(duration);
        }

        public void Refresh(float duration)
        {
            _remainingDuration = Mathf.Max(_remainingDuration, duration);
            EnsureEmitter();
        }

        private void Awake()
        {
            _player = GetComponent<Player>();
            EnsureEmitter();
        }

        private void EnsureEmitter()
        {
            if (_dripEmitterObject != null) return;

            _dripEmitterObject = new GameObject("GoldRain_WetDripEmitter");
            _dripEmitterObject.transform.SetParent(transform, false);
            _dripEmitterObject.transform.localPosition = new Vector3(0f, 1.1f, 0f); // уровень груди

            _dripParticleSystem = _dripEmitterObject.AddComponent<ParticleSystem>();
            _dripRenderer = _dripEmitterObject.GetComponent<ParticleSystemRenderer>();

            if (s_dripMaterial == null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("UI/Default");
                s_dripMaterial = new Material(shader) { name = "GoldRain_WetDripMat" };
            }
            _dripRenderer.material = s_dripMaterial;

            // Настройка эмиттера падающих капель с тела
            var main = _dripParticleSystem.main;
            main.duration = 1.0f;
            main.loop = true;
            main.startLifetime = 0.85f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;

            var emission = _dripParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 18f;

            var shape = _dripParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.5f, 0.9f, 0.4f); // контур туловища викинга

            // Золотисто-желтый цвет стекающих капель
            var col = _dripParticleSystem.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1.0f, 0.88f, 0.05f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.72f, 0.0f), 0.7f),
                    new GradientColorKey(new Color(0.9f, 0.60f, 0.0f), 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.9f, 0.0f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            _dripParticleSystem.Play();
        }

        private void Update()
        {
            _remainingDuration -= Time.deltaTime;
            if (_remainingDuration <= 0f || (_player != null && _player.IsDead()))
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (_dripEmitterObject != null)
            {
                Destroy(_dripEmitterObject);
            }
        }
    }
}
