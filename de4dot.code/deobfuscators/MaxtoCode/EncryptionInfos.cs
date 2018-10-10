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
			// 4F832868 = Mon, Apr 09 2012 20:20:24
			new EncryptionInfo {
				MagicLo = 0xAA913B87,
				MagicHi = 0xF28EE0A3,
				Version = EncryptionVersion.V4,
			},
			// 4F8E262C = Wed, 18 Apr 2012 02:25:48
			// 4FBE81DE = Thu, 24 May 2012 18:45:50
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
			// 50D367A5 = Mon, 12 Nov 2012 06:25:00
			new EncryptionInfo {
				MagicLo = 0x8A683B87,
				MagicHi = 0x828ECDA3,
				Version = EncryptionVersion.V7,
			},
			// 513D4492
			// 51413BD8
			// 51413D68
			// 5166DB4F
			new EncryptionInfo {
				MagicLo = 0x1A683B87,
				MagicHi = 0x128ECDA3,
				Version = EncryptionVersion.V8,
			},
			// 51927495
			new EncryptionInfo {
				MagicLo = 0x7A643B87,
				MagicHi = 0x624ECDA3,
				Version = EncryptionVersion.V8,
			},
			// 526BC020
			// 526BDD12
			// 5296E242
			// 52B2B2A3
			// 52B3043C
			// 53172907
			// 531729C4
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
			// 4F832868 = Mon, Apr 09 2012 20:20:24
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
			// 50D367A5 = Mon, 12 Nov 2012 06:25:00
			new EncryptionInfo {
				MagicLo = 0x8A731B13,
				MagicHi = 0x8723891F,
				Version = EncryptionVersion.V7,
			},
			// 513D4492
			// 51413BD8
			// 51413D68
			// 5166DB4F
			// 526BC020
			// 526BDD12
			// 5296E242
			// 52B2B2A3
			// 52B3043C
			// 53172907
			// 531729C4
			new EncryptionInfo {
				MagicLo = 0x1A731B13,
				MagicHi = 0x1723891F,
				Version = EncryptionVersion.V8,
			},
			// 51927495
			new EncryptionInfo {
				MagicLo = 0x7A731B13,
				MagicHi = 0x1723891F,
				Version = EncryptionVersion.V8,
			},
		};
	}
}
