using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace BusSim.Road
{
    /// <summary>
    /// Converts between road space (s along the spline, t lateral offset, positive to the right
    /// of travel) and world space. Planar: all direction maths is in XZ, Y comes from the
    /// road surface only. Assumes the road object has unit scale.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public class RoadSampler : MonoBehaviour
    {
        private const float MinDirectionSqrMagnitude = 1e-8f;

        [SerializeField] private RoadSettings settings;
        [SerializeField, Min(1)] private int nearestResolution = 16;
        [SerializeField, Min(1)] private int nearestIterations = 4;

        private SplineContainer container;

        public RoadSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        private SplineContainer Container
        {
            get
            {
                if (container == null)
                {
                    container = GetComponent<SplineContainer>();
                }
                return container;
            }
        }

        private Spline Spline => Container.Splines[0];

        public float SplineLength => Spline.GetLength();

        /// <summary>Usable road length in metres: the settings length, capped by the spline.</summary>
        public float Length => settings != null ? Mathf.Min(settings.roadLength, SplineLength) : SplineLength;

        /// <summary>Centre, forward and right at s. Forward and right are flat unit vectors.</summary>
        public void GetFrame(float s, out Vector3 centre, out Vector3 forward, out Vector3 right)
        {
            Spline spline = Spline;
            Transform splineTransform = Container.transform;
            float u = spline.ConvertIndexUnit(Mathf.Clamp(s, 0f, Length), PathIndexUnit.Distance, PathIndexUnit.Normalized);

            Vector3 localPosition = SplineUtility.EvaluatePosition(spline, u);
            Vector3 localTangent = SplineUtility.EvaluateTangent(spline, u);

            centre = splineTransform.TransformPoint(localPosition);
            centre.y = splineTransform.position.y + (settings != null ? settings.surfaceY : 0f);

            forward = splineTransform.TransformDirection(localTangent);
            forward.y = 0f;
            forward = forward.sqrMagnitude > MinDirectionSqrMagnitude ? forward.normalized : Vector3.forward;

            // Cross(up, forward) in Unity's left-handed axes.
            right = new Vector3(forward.z, 0f, -forward.x);
        }

        /// <summary>World position at road coordinates (s, t). Y is the road surface.</summary>
        public Vector3 GetPoint(float s, float t)
        {
            GetFrame(s, out Vector3 centre, out _, out Vector3 right);
            return centre + right * t;
        }

        public Vector3 GetForward(float s)
        {
            GetFrame(s, out _, out Vector3 forward, out _);
            return forward;
        }

        public Vector3 GetRight(float s)
        {
            GetFrame(s, out _, out _, out Vector3 right);
            return right;
        }

        /// <summary>Road coordinates of a world position. s is clamped to the road, t is not.</summary>
        public (float s, float t) ProjectToRoad(Vector3 worldPosition)
        {
            Spline spline = Spline;
            float3 localPosition = Container.transform.InverseTransformPoint(worldPosition);
            SplineUtility.GetNearestPoint(spline, localPosition, out float3 _, out float u, nearestResolution, nearestIterations);

            float s = Mathf.Clamp(spline.ConvertIndexUnit(u, PathIndexUnit.Normalized, PathIndexUnit.Distance), 0f, Length);
            GetFrame(s, out Vector3 centre, out _, out Vector3 right);

            Vector3 offset = worldPosition - centre;
            offset.y = 0f;
            return (s, Vector3.Dot(offset, right));
        }
    }
}
