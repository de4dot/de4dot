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
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.PE;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MPRESS {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "MPRESS";
		public const string THE_TYPE = "mp";
		public DeobfuscatorInfo()
			: base() {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		Version version;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		enum Version {
			Unknown,
			V0x,		// 0.71 - 0.99
			V1x_217,	// 1.x - 2.17
			V218,		// 2.18+
		}

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			if (version != Version.Unknown)
				val += 100;

			return val;
		}

		protected override void scanForObfuscator() {
			version = detectVersion();
			switch (version) {
			case Version.V0x: obfuscatorName += " v0.71 - v0.99"; break;
			case Version.V1x_217: obfuscatorName += " v1.x - v2.17"; break;
			case Version.V218: obfuscatorName += " v2.18+"; break;
			case Version.Unknown: break;
			default: throw new ApplicationException("Unknown version");
			}
		}

		class MethodInfo {
			public readonly string returnType;
			public readonly string parameters;
			public readonly string name;

			public MethodInfo(string returnType, string parameters)
				: this(returnType, parameters, null) {
			}

			public MethodInfo(string returnType, string parameters, string name) {
				this.returnType = returnType;
				this.parameters = parameters;
				this.name = name;
			}
		}

		static readonly string[] requiredFields = new string[] {
			"System.Reflection.Assembly",
		};
		static readonly MethodInfo[] methods_v0x = new MethodInfo[] {
			new MethodInfo("System.Void", "()", ".ctor"),
			new MethodInfo("System.Boolean", "(System.String,System.Byte[]&)"),
			new MethodInfo("System.Boolean", "(System.Byte[],System.Byte[]&)"),
			new MethodInfo("System.Int32", "(System.Byte[],System.Int32,System.Int32,System.Byte[],System.Int32,System.Int32)"),
			new MethodInfo("System.Int32", "(System.String[])"),
		};
		static readonly MethodInfo[] methods_v1x = new MethodInfo[] {
			new MethodInfo("System.Void", "()", ".ctor"),
			new MethodInfo("System.Boolean", "(System.String,System.Byte[]&)"),
			new MethodInfo("System.Boolean", "(System.Byte[],System.Byte[]&,System.Int32)"),
			new MethodInfo("System.Int32", "(System.Byte[],System.Byte[],System.Int32)"),
			new MethodInfo("System.Int32", "(System.String[])"),
		};
		Version detectVersion() {
			var ep = module.EntryPoint;
			if (ep == null || ep.Body == null)
				return Version.Unknown;
			var type = ep.DeclaringType;
			if (type == null)
				return Version.Unknown;
			if (!new FieldTypes(type).exactly(requiredFields))
				return Version.Unknown;
			if (module.Types.Count != 2)
				return Version.Unknown;
			if (module.Types[1] != type)
				return Version.Unknown;
			if (module.Types[0].Methods.Count != 0)
				return Version.Unknown;

			if (checkMethods(type, methods_v0x))
				return Version.V0x;
			if (checkMethods(type, methods_v1x)) {
				var lfMethod = DotNetUtils.getMethod(type, "System.Boolean", "(System.String,System.Byte[]&)");
				if (lfMethod != null) {
					if (DeobUtils.hasInteger(lfMethod, (int)Machine.amd64))
						return Version.V218;
					return Version.V1x_217;
				}
			}
			return Version.Unknown;
		}

		static bool checkMethods(TypeDefinition type, MethodInfo[] requiredMethods) {
			var methods = new List<MethodDefinition>(type.Methods);
			foreach (var info in requiredMethods) {
				if (!checkMethod(methods, info))
					return false;
			}
			return methods.Count == 0;
		}

		static bool checkMethod(List<MethodDefinition> methods, MethodInfo info) {
			foreach (var method in methods) {
				if (info.name != null && info.name != method.Name)
					continue;
				if (!DotNetUtils.isMethod(method, info.returnType, info.parameters))
					continue;

				methods.Remove(method);
				return true;
			}
			return false;
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || version == Version.Unknown)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.readModule(module);
			var peImage = new PeImage(fileData);
			var section = peImage.Sections[peImage.Sections.Length - 1];
			var offset = section.pointerToRawData;
			offset += 16;

			byte[] decompressed, compressed;
			int compressedLen;
			switch (version) {
			case Version.V0x:
				compressedLen = fileData.Length - (int)offset;
				compressed = peImage.offsetReadBytes(offset, compressedLen);
				decompressed = Lzmat.decompress_old(compressed);
				if (decompressed == null)
					throw new ApplicationException("LZMAT decompression failed");
				break;

			case Version.V1x_217:
			case Version.V218:
				if (peImage.FileHeader.machine == Machine.amd64 && version == Version.V218)
					offset = section.pointerToRawData + section.virtualSize;
				int decompressedLen = (int)peImage.offsetReadUInt32(offset);
				compressedLen = fileData.Length - (int)offset - 4;
				compressed = peImage.offsetReadBytes(offset + 4, compressedLen);
				decompressed = new byte[decompressedLen];
				uint decompressedLen2;
				if (Lzmat.decompress(decompressed, out decompressedLen2, compressed) != LzmatStatus.OK)
					throw new ApplicationException("LZMAT decompression failed");
				break;

			default:
				throw new ApplicationException("Unknown MPRESS version");
			}

			newFileData = decompressed;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			fixInvalidMvid();
		}

		void fixInvalidMvid() {
			if (module.Mvid == Guid.Empty) {
				var hash = new SHA1Managed().ComputeHash(Encoding.UTF8.GetBytes(module.ToString()));
				var guid = new Guid(BitConverter.ToInt32(hash, 0),
									BitConverter.ToInt16(hash, 4),
									BitConverter.ToInt16(hash, 6),
									hash[8], hash[9], hash[10], hash[11],
									hash[12], hash[13], hash[14], hash[15]);
				Log.v("Updating MVID: {0}", guid.ToString("B"));
				module.Mvid = guid;
			}
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			return new List<int>();
		}
	}
}
