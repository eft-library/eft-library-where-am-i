using System.Globalization;
using System.Text.RegularExpressions;
using where_am_i.Models;

namespace where_am_i.Services
{
    public static class PlayerLocationService
    {
        private static readonly Regex NumberPattern = new(
            @"[-+]?\d*\.?\d+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)
        );

        public static bool TryParse(string text, out PlayerLocation? location)
        {
            location = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var markerIndex = text.IndexOf("]_", StringComparison.Ordinal);
            var source = markerIndex >= 0 ? text[(markerIndex + 2)..] : text;
            var matches = NumberPattern.Matches(source);
            if (matches.Count < 7)
                return false;

            var values = new double[7];
            for (var index = 0; index < values.Length; index++)
            {
                if (!double.TryParse(
                    matches[index].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]
                ))
                {
                    return false;
                }
            }

            var (x, y, z, qx, qy, qz, qw) = (
                values[0], values[1], values[2], values[3], values[4], values[5], values[6]
            );
            var yaw = Math.Atan2(
                2 * (qw * qy - qz * qx),
                1 - 2 * (qy * qy + qx * qx)
            );

            if (yaw < 0)
                yaw += 2 * Math.PI;

            location = new PlayerLocation(x, y, z, yaw * 180 / Math.PI);
            return true;
        }

        public static LiveMapFloor? FindFloor(
            IEnumerable<LiveMapFloor> floors,
            PlayerLocation location
        )
        {
            var floorList = floors.ToList();
            var zoneFloor = floorList.FirstOrDefault(floor => floor.Zones.Any(zone =>
                IsBetween(location.X, zone.AreaXMin, zone.AreaXMax) &&
                IsBetween(location.Z, zone.AreaZMin, zone.AreaZMax) &&
                zone.OverrideMinY is double minY &&
                zone.OverrideMaxY is double maxY &&
                IsBetween(location.Y, minY, maxY)
            ));

            return zoneFloor
                ?? floorList.FirstOrDefault(floor =>
                    floor.MinY is double minY &&
                    floor.MaxY is double maxY &&
                    IsBetween(location.Y, minY, maxY)
                )
                ?? floorList.FirstOrDefault(floor => floor.MinY is null && floor.MaxY is null)
                ?? floorList.FirstOrDefault();
        }

        public static (double Latitude, double Longitude) ToMapPosition(
            string normalizedMapName,
            PlayerLocation location
        )
        {
            return normalizedMapName.ToLowerInvariant() switch
            {
                "factory" => (location.X, -location.Z),
                "the-lab" or "the-labyrinth" => (-location.X, location.Z),
                _ => (-location.Z, -location.X)
            };
        }

        public static double ToMapYaw(string normalizedMapName, double yaw)
        {
            return normalizedMapName.ToLowerInvariant() switch
            {
                "the-lab" or "the-labyrinth" => (yaw + 90) % 360,
                "factory" => (yaw + 270) % 360,
                _ => (yaw + 180) % 360
            };
        }

        private static bool IsBetween(double value, double first, double second)
        {
            return value >= Math.Min(first, second) && value <= Math.Max(first, second);
        }
    }
}
