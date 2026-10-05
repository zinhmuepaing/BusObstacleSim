using BusSim.Road;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace BusSim.Tests
{
    public class RoadSamplerTests
    {
        private const float PositionTolerance = 0.05f;
        private const float RoundTripTolerance = 0.1f;
        private const int RoundTripSamples = 200;
        private const int RoundTripSeed = 1234;

        private GameObject roadObject;
        private RoadSettings settings;
        private RoadSampler sampler;

        private void BuildRoad(params float3[] knots)
        {
            settings = ScriptableObject.CreateInstance<RoadSettings>();
            roadObject = new GameObject("TestRoad");
            sampler = roadObject.AddComponent<RoadSampler>();
            sampler.Settings = settings;

            SplineContainer container = roadObject.GetComponent<SplineContainer>();
            Spline spline = container.Splines[0];
            spline.Clear();
            foreach (float3 knot in knots)
            {
                spline.Add(new BezierKnot(knot), TangentMode.AutoSmooth);
            }
            settings.roadLength = spline.GetLength();
        }

        [TearDown]
        public void TearDown()
        {
            if (roadObject != null)
            {
                Object.DestroyImmediate(roadObject);
            }
            if (settings != null)
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void StraightRoad_LengthMatchesSpline()
        {
            BuildRoad(new float3(0, 0, 0), new float3(0, 0, 500), new float3(0, 0, 1000));
            Assert.AreEqual(1000f, sampler.Length, PositionTolerance);
        }

        [Test]
        public void StraightRoad_PointsFollowCentrelineAndRightIsPositiveX()
        {
            BuildRoad(new float3(0, 0, 0), new float3(0, 0, 500), new float3(0, 0, 1000));

            Vector3 centre = sampler.GetPoint(500f, 0f);
            Assert.AreEqual(0f, centre.x, PositionTolerance);
            Assert.AreEqual(500f, centre.z, PositionTolerance);

            Assert.AreEqual(1.75f, sampler.GetPoint(500f, 1.75f).x, PositionTolerance);
            Assert.AreEqual(-1.75f, sampler.GetPoint(500f, -1.75f).x, PositionTolerance);
            Assert.AreEqual(0f, (sampler.GetForward(500f) - Vector3.forward).magnitude, PositionTolerance);
            Assert.AreEqual(0f, (sampler.GetRight(500f) - Vector3.right).magnitude, PositionTolerance);
        }

        [Test]
        public void StraightRoad_ProjectToRoadReturnsSAndSignedT()
        {
            BuildRoad(new float3(0, 0, 0), new float3(0, 0, 500), new float3(0, 0, 1000));

            (float s, float t) = sampler.ProjectToRoad(new Vector3(1.75f, 0f, 500f));
            Assert.AreEqual(500f, s, RoundTripTolerance);
            Assert.AreEqual(1.75f, t, PositionTolerance);

            (s, t) = sampler.ProjectToRoad(new Vector3(-1.75f, 0f, 250f));
            Assert.AreEqual(250f, s, RoundTripTolerance);
            Assert.AreEqual(-1.75f, t, PositionTolerance);
        }

        [Test]
        public void LaneCentres_MatchSpec()
        {
            BuildRoad(new float3(0, 0, 0), new float3(0, 0, 1000));
            Assert.AreEqual(-1.75f, settings.GetLaneCentreT(0), PositionTolerance);
            Assert.AreEqual(1.75f, settings.GetLaneCentreT(1), PositionTolerance);
            Assert.AreEqual(7f, settings.RoadWidth, PositionTolerance);
        }

        [Test]
        public void CurvedRoad_GetPointThenProjectRoundTrips()
        {
            BuildRoad(
                new float3(0, 0, 0), new float3(0, 0, 150), new float3(40, 0, 330),
                new float3(40, 0, 520), new float3(-10, 0, 720), new float3(-10, 0, 1000));

            System.Random rng = new System.Random(RoundTripSeed);
            for (int i = 0; i < RoundTripSamples; i++)
            {
                float s = (float)rng.NextDouble() * sampler.Length;
                float t = ((float)rng.NextDouble() - 0.5f) * settings.RoadWidth;

                (float projectedS, float projectedT) = sampler.ProjectToRoad(sampler.GetPoint(s, t));

                Assert.AreEqual(s, projectedS, RoundTripTolerance, $"s mismatch at sample {i}");
                Assert.AreEqual(t, projectedT, PositionTolerance, $"t mismatch at sample {i}");
            }
        }
    }
}
