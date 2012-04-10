/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using de4dot.PE;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	class IniFile {
		Dictionary<string, string> nameToValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public string this[string name] {
			get {
				string value;
				nameToValue.TryGetValue(name, out value);
				return value;
			}
		}

		public IniFile(byte[] data) {
			using (var reader = new StreamReader(new MemoryStream(data), Encoding.UTF8)) {
				while (true) {
					var line = reader.ReadLine();
					if (line == null)
						break;
					var match = Regex.Match(line, @"^([^=]+)=([^;]+);?\s*$");
					if (match.Groups.Count < 3)
						continue;
					var name = match.Groups[1].ToString().Trim();
					var value = match.Groups[2].ToString().Trim();
					nameToValue[name] = value;
				}
			}
		}
	}

	// Unpacks "application mode" files (DNR 3.x)
	class ApplicationModeUnpacker {
		static byte[] key1 = new byte[32] {
			0x6B, 0x6C, 0xA7, 0x24, 0x25, 0x37, 0x67, 0x68,
			0x4A, 0x2F, 0x28, 0x29, 0x33, 0x77, 0x34, 0x35,
			0x5A, 0x5A, 0x48, 0x57, 0x24, 0x35, 0x24, 0x25,
			0x26, 0x67, 0x77, 0x53, 0x41, 0x44, 0x46, 0x32,
		};
		static byte[] iv1 = new byte[16] {
			0x73, 0x64, 0xA7, 0x35, 0x24, 0xA7, 0x26, 0x67,
			0x34, 0x35, 0x37, 0x21, 0x32, 0x33, 0x6E, 0x6D,
		};
		static byte[] key2 = new byte[32] {
			0x28, 0x24, 0x29, 0x28, 0x2F, 0x29, 0x28, 0x29,
			0x3D, 0x66, 0x67, 0x35, 0x35, 0x6A, 0x6D, 0x2C,
			0xA7, 0x39, 0x38, 0x2A, 0x6A, 0x67, 0x74, 0x36,
			0x35, 0x3D, 0xA7, 0x43, 0x33, 0x33, 0x24, 0x74,
		};
		static byte[] iv2 = new byte[16] {
			0x67, 0x26, 0x35, 0xA7, 0x24, 0xA7, 0x37, 0x21,
			0x73, 0x33, 0x6E, 0x6D, 0x34, 0x32, 0x64, 0x35,
		};

		PeImage peImage;
		List<UnpackedFile> satelliteAssemblies = new List<UnpackedFile>();
		uint[] sizes;
		string[] filenames;
		bool shouldUnpack;

		public IEnumerable<UnpackedFile> EmbeddedAssemblies {
			get { return satelliteAssemblies; }
		}

		public ApplicationModeUnpacker(PeImage peImage) {
			this.peImage = peImage;
		}

		public byte[] unpack() {
			byte[] data = null;
			try {
				data = unpack2();
			}
			catch {
			}
			if (data != null)
				return data;

			if (shouldUnpack)
				Log.w("Could not unpack the file");
			return null;
		}

		byte[] unpack2() {
			shouldUnpack = false;
			uint headerOffset = peImage.ImageLength - 12;
			uint offsetEncryptedAssembly = checkOffset(peImage.offsetReadUInt32(headerOffset));
			uint ezencryptionLibLength = peImage.offsetReadUInt32(headerOffset + 4);
			uint iniFileLength = peImage.offsetReadUInt32(headerOffset + 8);

			uint offsetClrVersionNumber = checked(offsetEncryptedAssembly - 12);
			uint iniFileOffset = checked(headerOffset - iniFileLength);
			uint ezencryptionLibOffset = checked(iniFileOffset - ezencryptionLibLength);

			uint clrVerMajor = peImage.offsetReadUInt32(offsetClrVersionNumber);
			uint clrVerMinor = peImage.offsetReadUInt32(offsetClrVersionNumber + 4);
			uint clrVerBuild = peImage.offsetReadUInt32(offsetClrVersionNumber + 8);
			if (clrVerMajor <= 0 || clrVerMajor >= 20 || clrVerMinor >= 20 || clrVerBuild >= 1000000)
				return null;

			var settings = new IniFile(decompress2(peImage.offsetReadBytes(iniFileOffset, (int)iniFileLength)));
			sizes = getSizes(settings["General_App_Satellite_Assemblies_Sizes"]);
			if (sizes == null || sizes.Length <= 1)
				return null;
			shouldUnpack = true;
			if (sizes[0] != offsetEncryptedAssembly)
				return null;
			filenames = settings["General_App_Satellite_Assemblies"].Split('|');
			if (sizes.Length - 1 != filenames.Length)
				return null;

			byte[] ezencryptionLibData = decompress1(peImage.offsetReadBytes(ezencryptionLibOffset, (int)ezencryptionLibLength));
			var ezencryptionLibModule = ModuleDefinition.ReadModule(new MemoryStream(ezencryptionLibData));
			var decrypter = new ApplicationModeDecrypter(ezencryptionLibModule);
			if (!decrypter.Detected)
				return null;

			var mainAssembly = unpackEmbeddedFile(0, decrypter);
			decrypter.MemoryPatcher.patch(mainAssembly.data);
			for (int i = 1; i < filenames.Length; i++)
				satelliteAssemblies.Add(unpackEmbeddedFile(i, decrypter));

			clearDllBit(mainAssembly.data);
			return mainAssembly.data;
		}

		static void clearDllBit(byte[] peImageData) {
			var mainPeImage = new PeImage(peImageData);
			uint characteristicsOffset = mainPeImage.FileHeaderOffset + 18;
			ushort characteristics = mainPeImage.offsetReadUInt16(characteristicsOffset);
			characteristics &= 0xDFFF;
			characteristics |= 2;
			mainPeImage.offsetWriteUInt16(characteristicsOffset, characteristics);
		}

		UnpackedFile unpackEmbeddedFile(int index, ApplicationModeDecrypter decrypter) {
			uint offset = 0;
			for (int i = 0; i < index + 1; i++)
				offset += sizes[i];
			string filename = Win32Path.GetFileName(filenames[index]);
			var data = peImage.offsetReadBytes(offset, (int)sizes[index + 1]);
			data = DeobUtils.aesDecrypt(data, decrypter.AssemblyKey, decrypter.AssemblyIv);
			data = decompress(data);
			return new UnpackedFile(filename, data);
		}

		static uint[] getSizes(string sizes) {
			if (sizes == null)
				return null;
			var list = new List<uint>();
			foreach (var num in sizes.Split('|'))
				list.Add(uint.Parse(num));
			return list.ToArray();
		}

		uint checkOffset(uint offset) {
			if (offset >= peImage.ImageLength)
				throw new Exception();
			return offset;
		}

		static byte[] decompress1(byte[] data) {
			return decompress(decrypt1(data));
		}

		static byte[] decompress2(byte[] data) {
			return decompress(decrypt2(data));
		}

		static byte[] decompress(byte[] data) {
			if (!QuickLZ.isCompressed(data))
				return data;
			return QuickLZ.decompress(data);
		}

		static byte[] decrypt1(byte[] data) {
			return DeobUtils.aesDecrypt(data, key1, iv1);
		}

		static byte[] decrypt2(byte[] data) {
			return DeobUtils.aesDecrypt(data, key2, iv2);
		}
	}
}
