using System.Collections.Generic;
using UnityEngine;

namespace BusSim.Road
{
    /// <summary>Marked stretches of the road (bus stops, school zones) used by zone-only obstacles.</summary>
    public class RoadZones : MonoBehaviour
    {
        [SerializeField] private List<RoadZone> zones = new List<RoadZone>();

        public IReadOnlyList<RoadZone> Zones => zones;

        public void SetZones(IEnumerable<RoadZone> newZones)
        {
            zones.Clear();
            zones.AddRange(newZones);
        }

        public bool IsInside(ZoneType type, float s)
        {
            foreach (RoadZone zone in zones)
            {
                if (zone.type == type && s >= zone.sStart && s <= zone.sEnd)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
