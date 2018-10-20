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

namespace de4dot.code.deobfuscators.MaxtoCode {
	class EncryptionInfo {
		public uint MagicLo { get; set; }
		public uint MagicHi { get; set; }
		public EncryptionVersion Version { get; set; }
	}

	static class EncryptionInfos {
		public static readonly EncryptionInfo[] Rva900h = new EncryptionInfo[] {
			// PE header timestamp
			// 462FA2D2 = Wed, 25 Apr 2007 18:49:54 (3.20)
			// 471299D3 = Sun, 14 Oct 2007 22:36:03 (3.22)
			new EncryptionInfo {
				MagicLo = 0xA098B387,
				MagicHi = 0x1E8EBCA3,
				Version = EncryptionVersion.V1,
			},
			// 482384FB = Thu, 08 May 2008 22:55:55 (3.36)
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0x1E8EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4A5EEC64 = Thu, 16 Jul 2009 09:01:24
			// 4C6220EC = Wed, 11 Aug 2010 04:02:52
			// 4C622357 = Wed, 11 Aug 2010 04:13:11
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0x128EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4C6E4605 = Fri, 20 Aug 2010 09:08:21
			// 4D0E220D = Sun, 19 Dec 2010 15:17:33
			// 4DC2FC75 = Thu, 05 May 2011 19:37:25
			// 4DFA3D5D = Thu, 16 Jun 2011 17:29:01
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0xF28EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4DC2FE0C = Thu, 05 May 2011 19:44:12
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0xF28EEAA3,
				Version = EncryptionVersion.V2,
			},
			// 4ED76740 = Thu, 01 Dec 2011 11:38:40
			// 4EE1FAD1 = Fri, 09 Dec 2011 12:10:57
			new EncryptionInfo {
				MagicLo = 0xAA983B87,
				MagicHi = 0xF28EECA3,
				Version = EncryptionVersion.V3,
			},
			// 4F832868 = Mon, 09 Apr 2012 18:20:24
			new EncryptionInfo {
				MagicLo = 0xAA913B87,
				MagicHi = 0xF28EE0A3,
				Version = EncryptionVersion.V4,
			},
			// 4F8E262C = Wed, 18 Apr 2012 02:25:48
			// 4FBE81DE = Thu, 24 May 2012 18:45:50
			// 4FCEBD7B = Wed, 06 Jun 2012 02:16:27 (untested)
			new EncryptionInfo {
				MagicLo = 0xBA983B87,
				MagicHi = 0xF28EDDA3,
				Version = EncryptionVersion.V5,
			},
			// 50A0963C = Mon, 12 Nov 2012 06:25:00
			new EncryptionInfo {
				MagicLo = 0xBA683B87,
				MagicHi = 0xF28ECDA3,
				Version = EncryptionVersion.V6,
			},
			// 50D367A5 = Thu, 20 Dec 2012 19:31:49
			new EncryptionInfo {
				MagicLo = 0x8A683B87,
				MagicHi = 0x828ECDA3,
				Version = EncryptionVersion.V7,
			},
			// 513D4492 = Mon, 11 Mar 2013 02:42:26
			// 513D7124 = Mon, 11 Mar 2013 05:52:36
			// 51413BD8 = Thu, 14 Mar 2013 02:54:16
			// 51413D68 = Thu, 14 Mar 2013 03:00:56
			// 5166DB4F = Thu, 11 Apr 2013 15:48:31
			new EncryptionInfo {
				MagicLo = 0x1A683B87,
				MagicHi = 0x128ECDA3,
				Version = EncryptionVersion.V8,
			},
			// 51927495 = Tue, 14 May 2013 17:29:57
			new EncryptionInfo {
				MagicLo = 0x7A643B87,
				MagicHi = 0x624ECDA3,
				Version = EncryptionVersion.V8,
			},
			// 526BC020 = Sat, 26 Oct 2013 13:14:08
			// 526BDD12 = Sat, 26 Oct 2013 15:17:38
			// 5296E242 = Thu, 28 Nov 2013 06:27:14
			// 52B2B2A3 = Thu, 19 Dec 2013 08:47:31
			// 52B3043C = Thu, 19 Dec 2013 14:35:40
			// 53172907 = Wed, 05 Mar 2014 13:39:19
			// 531729C4 = Wed, 05 Mar 2014 13:42:28
			// 55F5B112 = Sun, 13 Sep 2015 17:23:30 (3.80) (untested)
			// 5892EF00 = Thu, 02 Feb 2017 08:34:08 (3.84) (untested)
			// 59995527 = Sun, 20 Aug 2017 09:23:51 (3.86) (untested)
			// 5AAF874A = Mon, 19 Mar 2018 09:47:54 (3.87) (untested)
			// 5B37D998 = Sat, 30 Jun 2018 19:27:20 (Beta above 3.87) (untested)
			new EncryptionInfo {
				MagicLo = 0x9A683B87,
				MagicHi = 0x928ECDA3,
				Version = EncryptionVersion.V8,
			},
		};

		public static readonly EncryptionInfo[] McKey8C0h = new EncryptionInfo[] {
			// 462FA2D2 = Wed, 25 Apr 2007 18:49:54 (3.20)
			// 471299D3 = Sun, 14 Oct 2007 22:36:03 (3.22)
			new EncryptionInfo {
				MagicLo = 0x6AA13B13,
				MagicHi = 0xD72B991F,
				Version = EncryptionVersion.V1,
			},
			// 482384FB = Thu, 08 May 2008 22:55:55 (3.36)
			new EncryptionInfo {
				MagicLo = 0x6A713B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V2,
			},
			// 4A5EEC64 = Thu, 16 Jul 2009 09:01:24
			// 4C6220EC = Wed, 11 Aug 2010 04:02:52
			// 4C622357 = Wed, 11 Aug 2010 04:13:11
			// 4C6E4605 = Fri, 20 Aug 2010 09:08:21
			// 4D0E220D = Sun, 19 Dec 2010 15:17:33
			// 4DC2FC75 = Thu, 05 May 2011 19:37:25
			// 4DC2FE0C = Thu, 05 May 2011 19:44:12
			// 4DFA3D5D = Thu, 16 Jun 2011 17:29:01
			new EncryptionInfo {
				MagicLo = 0x6A713B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V2,
			},
			// 4ED76740 = Thu, 01 Dec 2011 11:38:40
			// 4EE1FAD1 = Fri, 09 Dec 2011 12:10:57
			new EncryptionInfo {
				MagicLo = 0x6A731B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V3,
			},
			// 4F832868 = Mon, 09 Apr 2012 18:20:24
			new EncryptionInfo {
				MagicLo = 0x6AD31B13,
				MagicHi = 0xD72B8A1F,
				Version = EncryptionVersion.V4,
			},
			// 4F8E262C = Wed, 18 Apr 2012 02:25:48
			new EncryptionInfo {
				MagicLo = 0xAA731B13,
				MagicHi = 0xD723891F,
				Version = EncryptionVersion.V5,
			},
			// 4FCEBD7B = Wed, 06 Jun 2012 02:16:27 (untested)
			new EncryptionInfo {
				MagicLo = 0x64D6CE53,
				MagicHi = 0xDEC2844E,
				Version = EncryptionVersion.V5,
			},
			// 50D367A5 = Thu, 20 Dec 2012 19:31:49
			new EncryptionInfo {
				MagicLo = 0x8A731B13,
				MagicHi = 0x8723891F,
				Version = EncryptionVersion.V7,
			},
			// 513D4492 = Mon, 11 Mar 2013 02:42:26
			// 513D7124 = Mon, 11 Mar 2013 05:52:36
			// 51413BD8 = Thu, 14 Mar 2013 02:54:16
			// 51413D68 = Thu, 14 Mar 2013 03:00:56
			// 5166DB4F = Thu, 11 Apr 2013 15:48:31
			// 526BC020 = Sat, 26 Oct 2013 13:14:08
			// 526BDD12 = Sat, 26 Oct 2013 15:17:38
			// 5296E242 = Thu, 28 Nov 2013 06:27:14
			// 52B2B2A3 = Thu, 19 Dec 2013 08:47:31
			// 52B3043C = Thu, 19 Dec 2013 14:35:40
			// 53172907 = Wed, 05 Mar 2014 13:39:19
			// 531729C4 = Wed, 05 Mar 2014 13:42:28
			// 5892EF00 = Thu, 02 Feb 2017 08:34:08 (3.84) (untested)
			new EncryptionInfo {
				MagicLo = 0x1A731B13,
				MagicHi = 0x1723891F,
				Version = EncryptionVersion.V8,
			},
			// 51927495 = Tue, 14 May 2013 17:29:57
			new EncryptionInfo {
				MagicLo = 0x7A731B13,
				MagicHi = 0x1723891F,
				Version = EncryptionVersion.V8,
			},
			// 55F5B112 = Sun, 13 Sep 2015 17:23:30 (3.80) (untested)
			new EncryptionInfo {
				MagicLo = 0xDD980712,
				MagicHi = 0xF36F3511,
				Version = EncryptionVersion.V8,
			},
			// 59995527 = Sun, 20 Aug 2017 09:23:51 (3.86) (untested)
			new EncryptionInfo {
				MagicLo = 0x49DC30A2,
				MagicHi = 0x3BE51694,
				Version = EncryptionVersion.V8,
			},
			// 5AAF874A = Mon, 19 Mar 2018 09:47:54 (3.87) (untested)
			new EncryptionInfo {
				MagicLo = 0x58425DA8,
				MagicHi = 0xDF80B317,
				Version = EncryptionVersion.V8,
			},
			// 5B37D998 = Sat, 30 Jun 2018 19:27:20 (Beta above 3.87) (untested)
			new EncryptionInfo {
				MagicLo = 0xC00CA8DC,
				MagicHi = 0xEFBCF433,
				Version = EncryptionVersion.V8,
			},
		};
	}
}
