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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet;
using dnlib.DotNet.MD;

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

		IPEImage peImage;
		List<UnpackedFile> satelliteAssemblies = new List<UnpackedFile>();
		uint[] sizes;
		string[] filenames;
		bool shouldUnpack;

		public IEnumerable<UnpackedFile> EmbeddedAssemblies {
			get { return satelliteAssemblies; }
		}

		public ApplicationModeUnpacker(IPEImage peImage) {
			this.peImage = peImage;
		}

		public byte[] Unpack() {
			byte[] data = null;
			MyPEImage myPeImage = null;
			try {
				myPeImage = new MyPEImage(peImage);
				data = Unpack2(myPeImage);
			}
			catch {
			}
			finally {
				if (myPeImage != null)
					myPeImage.Dispose();
			}
			if (data != null)
				return data;

			if (shouldUnpack)
				Logger.w("Could not unpack file: {0}", peImage.FileName ?? "(unknown filename)");
			return null;
		}

		byte[] Unpack2(MyPEImage peImage) {
			shouldUnpack = false;
			uint headerOffset = (uint)peImage.Length - 12;
			uint offsetEncryptedAssembly = CheckOffset(peImage, peImage.OffsetReadUInt32(headerOffset));
			uint ezencryptionLibLength = peImage.OffsetReadUInt32(headerOffset + 4);
			uint iniFileLength = peImage.OffsetReadUInt32(headerOffset + 8);

			uint offsetClrVersionNumber = checked(offsetEncryptedAssembly - 12);
			uint iniFileOffset = checked(headerOffset - iniFileLength);
			uint ezencryptionLibOffset = checked(iniFileOffset - ezencryptionLibLength);

			uint clrVerMajor = peImage.OffsetReadUInt32(offsetClrVersionNumber);
			uint clrVerMinor = peImage.OffsetReadUInt32(offsetClrVersionNumber + 4);
			uint clrVerBuild = peImage.OffsetReadUInt32(offsetClrVersionNumber + 8);
			if (clrVerMajor <= 0 || clrVerMajor >= 20 || clrVerMinor >= 20 || clrVerBuild >= 1000000)
				return null;

			var settings = new IniFile(Decompress2(peImage.OffsetReadBytes(iniFileOffset, (int)iniFileLength)));
			sizes = GetSizes(settings["General_App_Satellite_Assemblies_Sizes"]);
			if (sizes == null || sizes.Length <= 1)
				return null;
			shouldUnpack = true;
			if (sizes[0] != offsetEncryptedAssembly)
				return null;
			filenames = settings["General_App_Satellite_Assemblies"].Split('|');
			if (sizes.Length - 1 != filenames.Length)
				return null;

			byte[] ezencryptionLibData = Decompress1(peImage.OffsetReadBytes(ezencryptionLibOffset, (int)ezencryptionLibLength));
			var ezencryptionLibModule = ModuleDefMD.Load(ezencryptionLibData);
			var decrypter = new ApplicationModeDecrypter(ezencryptionLibModule);
			if (!decrypter.Detected)
				return null;

			var mainAssembly = UnpackEmbeddedFile(peImage, 0, decrypter);
			decrypter.MemoryPatcher.Patch(mainAssembly.data);
			for (int i = 1; i < filenames.Length; i++)
				satelliteAssemblies.Add(UnpackEmbeddedFile(peImage, i, decrypter));

			ClearDllBit(mainAssembly.data);
			return mainAssembly.data;
		}

		static void ClearDllBit(byte[] peImageData) {
			using (var mainPeImage = new MyPEImage(peImageData)) {
				uint characteristicsOffset = (uint)mainPeImage.PEImage.ImageNTHeaders.FileHeader.StartOffset + 18;
				ushort characteristics = mainPeImage.OffsetReadUInt16(characteristicsOffset);
				characteristics &= 0xDFFF;
				characteristics |= 2;
				mainPeImage.OffsetWriteUInt16(characteristicsOffset, characteristics);
			}
		}

		UnpackedFile UnpackEmbeddedFile(MyPEImage peImage, int index, ApplicationModeDecrypter decrypter) {
			uint offset = 0;
			for (int i = 0; i < index + 1; i++)
				offset += sizes[i];
			string filename = Win32Path.GetFileName(filenames[index]);
			var data = peImage.OffsetReadBytes(offset, (int)sizes[index + 1]);
			data = DeobUtils.AesDecrypt(data, decrypter.AssemblyKey, decrypter.AssemblyIv);
			data = Decompress(data);
			return new UnpackedFile(filename, data);
		}

		static uint[] GetSizes(string sizes) {
			if (sizes == null)
				return null;
			var list = new List<uint>();
			foreach (var num in sizes.Split('|'))
				list.Add(uint.Parse(num));
			return list.ToArray();
		}

		uint CheckOffset(MyPEImage peImage, uint offset) {
			if (offset >= peImage.Length)
				throw new Exception();
			return offset;
		}

		static byte[] Decompress1(byte[] data) {
			return Decompress(Decrypt1(data));
		}

		static byte[] Decompress2(byte[] data) {
			return Decompress(Decrypt2(data));
		}

		static byte[] Decompress(byte[] data) {
			if (!QuickLZ.IsCompressed(data))
				return data;
			return QuickLZ.Decompress(data);
		}

		static byte[] Decrypt1(byte[] data) {
			return DeobUtils.AesDecrypt(data, key1, iv1);
		}

		static byte[] Decrypt2(byte[] data) {
			return DeobUtils.AesDecrypt(data, key2, iv2);
		}
	}
}
