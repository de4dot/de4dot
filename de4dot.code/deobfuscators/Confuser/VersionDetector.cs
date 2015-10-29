/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;

namespace de4dot.code.deobfuscators.Confuser {
	class VersionDetector {
		int minRev = -1, maxRev = int.MaxValue;

		static readonly int[] revs = new int[] {
			42519, 42915, 42916, 42917, 42919, 42960, 43055, 45527,
			48493, 48509, 48717, 48718, 48771, 48832, 48863, 49238,
			49299, 49300, 49966, 50359, 50378, 50661, 54225, 54254,
			54312, 54431, 54564, 54566, 54574, 55346, 55604, 55608,
			55609, 55764, 55802, 56535, 57588, 57593, 57699, 57778,
			57884, 58004, 58172, 58446, 58564, 58741, 58802, 58804,
			58817, 58852, 58857, 58919, 59014, 60052, 60054, 60111,
			60408, 60785, 60787, 61954, 62284, 64574, 65189, 65282,
			65297, 65298, 65299, 65747, 66631, 66853, 66883, 67015,
			67058, 69339, 69666, 70489, 71742, 71743, 71847, 72164,
			72434, 72819, 72853, 72868, 72989, 73404, 73430, 73477,
			73479, 73566, 73593, 73605, 73740, 73764, 73770, 73791,
			73822, 74021, 74184, 74476, 74482, 74520, 74574, 74578,
			74637, 74708, 74788, 74816, 74852, 75056, 75076, 75077,
			75131, 75152, 75158, 75184, 75257, 75267, 75288, 75291,
			75306, 75318, 75349, 75367, 75369, 75402, 75459, 75461,
			75573, 75719, 75720, 75725, 75806, 75807, 75926, 76101,
			76119, 76163, 76186, 76271, 76360, 76509, 76542, 76548,
			76558, 76580, 76656, 76871, 76923, 76924, 76933, 76934,
			76972, 76974, 77124, 77172, 77447, 77501, 78056, 78072,
			78086, 78196, 78197, 78342, 78363, 78377, 78612, 78638,
			78642, 78730, 78731, 78962, 78963, 78964, 79256, 79257,
			79258, 79440, 79630, 79631, 79632, 79634, 79642,
		};

		static Dictionary<int, Version> revToVersion = new Dictionary<int, Version> {
			{ 42519, new Version(1, 0) },	// May 01 2010
			{ 49299, new Version(1, 1) },	// Jul 13 2010
			{ 50661, new Version(1, 2) },	// Jul 23 2010
			{ 54574, new Version(1, 3) },	// Aug 31 2010
			{ 55609, new Version(1, 4) },	// Sep 15 2010
			{ 58919, new Version(1, 5) },	// Nov 29 2010
			{ 60787, new Version(1, 6) },	// Jan 10 2011
			{ 72989, new Version(1, 7) },	// Mar 09 2012
			{ 75131, new Version(1, 8) },	// May 31 2012
			{ 75461, new Version(1, 9) },	// Jun 23 2012
		};

		static VersionDetector() {
			Version currentVersion = null;
			int prevRev = -1;
			foreach (var rev in revs) {
				if (rev <= prevRev)
					throw new ApplicationException();
				Version version;
				if (revToVersion.TryGetValue(rev, out version))
					currentVersion = version;
				else if (currentVersion == null)
					throw new ApplicationException();
				else
					revToVersion[rev] = currentVersion;
				prevRev = rev;
			}
		}

		public void AddRevs(int min, int max) {
			if (min < 0 || max < 0 || min > max)
				throw new ArgumentOutOfRangeException();
			if (!revToVersion.ContainsKey(min) || (max != int.MaxValue && !revToVersion.ContainsKey(max)))
				throw new ArgumentOutOfRangeException();

			if (min > minRev)
				minRev = min;
			if (max < maxRev)
				maxRev = max;
		}

		public void SetVersion(Version version) {
			if (version == null)
				return;
			int minRev = int.MaxValue, maxRev = int.MinValue;
			foreach (var kv in revToVersion) {
				if (kv.Value.Major != version.Major || kv.Value.Minor != version.Minor)
					continue;
				if (minRev > kv.Key)
					minRev = kv.Key;
				if (maxRev < kv.Key)
					maxRev = kv.Key;
			}
			if (minRev == int.MaxValue)
				return;
			if (maxRev == revs[revs.Length - 1])
				maxRev = int.MaxValue;
			AddRevs(minRev, maxRev);
		}

		public string GetVersionString() {
			if (minRev > maxRev || minRev < 0)
				return null;
			var minVersion = revToVersion[minRev];
			if (maxRev == int.MaxValue) {
				var latestRev = revs[revs.Length - 1];
				if (minRev == latestRev)
					return string.Format("v{0}.{1} (r{2})", minVersion.Major, minVersion.Minor, minRev);
				var latestVersion = revToVersion[latestRev];
				if (minVersion == latestVersion)
					return string.Format("v{0}.{1} (r{2}+)", minVersion.Major, minVersion.Minor, minRev);
				return string.Format("v{0}.{1}+ (r{2}+)", minVersion.Major, minVersion.Minor, minRev);
			}
			var maxVersion = revToVersion[maxRev];
			if (minVersion == maxVersion) {
				if (minRev == maxRev)
					return string.Format("v{0}.{1} (r{2})", minVersion.Major, minVersion.Minor, minRev);
				return string.Format("v{0}.{1} (r{2}-r{3})", minVersion.Major, minVersion.Minor, minRev, maxRev);
			}
			return string.Format("v{0}.{1} - v{2}.{3} (r{4}-r{5})", minVersion.Major, minVersion.Minor, maxVersion.Major, maxVersion.Minor, minRev, maxRev);
		}

		public override string ToString() {
			return GetVersionString() ?? "<no version>";
		}
	}
}
