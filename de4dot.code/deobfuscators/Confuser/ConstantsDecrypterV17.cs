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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	// From v1.7 r74708 to v1.8 r75349
	class ConstantsDecrypterV17 : ConstantsDecrypterBase {
		MethodDef initMethod;
		ConfuserVersion version = ConfuserVersion.Unknown;
		string resourceName;
		int keyArraySize = 8;

		enum ConfuserVersion {
			Unknown,
			v17_r74708_normal,
			v17_r74708_dynamic,
			v17_r74708_native,
			v17_r74788_normal,
			v17_r74788_dynamic,
			v17_r74788_native,
			v17_r74816_normal,
			v17_r74816_dynamic,
			v17_r74816_native,
			v17_r75056_normal,
			v17_r75056_dynamic,
			v17_r75056_native,
			v18_r75257_normal,
			v18_r75257_dynamic,
			v18_r75257_native,
			// 1.8 r75349 was the last version using this constants encrypter. Starting
			// from 1.8 r75367, the new constants encrypter (generic methods) is used.
		}

		class DecrypterInfoV17 : DecrypterInfo {
			public readonly ConfuserVersion version = ConfuserVersion.Unknown;
			public uint key4, key5;

			public DecrypterInfoV17(ConfuserVersion version, MethodDef decryptMethod) {
				this.version = version;
				this.decryptMethod = decryptMethod;
			}

			protected override bool InitializeKeys() {
				if (!FindKey0(decryptMethod, out key0))
					return false;
				if (!FindKey1_v17(decryptMethod, out key1))
					return false;
				if (!FindKey2Key3(decryptMethod, out key2, out key3))
					return false;
				if (!FindKey4(decryptMethod, out key4))
					return false;
				if (!FindKey5(decryptMethod, out key5))
					return false;

				return true;
			}

			static bool FindKey1_v17(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 4; i++) {
					var stloc = instrs[i];
					if (!stloc.IsStloc())
						continue;
					var ldci4 = instrs[i + 1];
					if (!ldci4.IsLdcI4())
						continue;
					var ldcloc = instrs[i + 2];
					if (!ldcloc.IsLdloc())
						continue;
					if (stloc.GetLocal(method.Body.Variables) != ldcloc.GetLocal(method.Body.Variables))
						continue;
					if (instrs[i + 3].OpCode.Code != Code.Xor)
						continue;
					if (!instrs[i + 4].IsStloc())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}

			bool FindKey4(MethodDef method, out uint key) {
				switch (version) {
				case ConfuserVersion.v17_r74708_normal:
				case ConfuserVersion.v17_r74788_normal:
				case ConfuserVersion.v17_r74816_normal:
				case ConfuserVersion.v17_r75056_normal:
				case ConfuserVersion.v18_r75257_normal:
					return FindKey4_normal(method, out key);
				case ConfuserVersion.v17_r74708_dynamic:
				case ConfuserVersion.v17_r74708_native:
				case ConfuserVersion.v17_r74788_dynamic:
				case ConfuserVersion.v17_r74788_native:
				case ConfuserVersion.v17_r74816_dynamic:
				case ConfuserVersion.v17_r74816_native:
				case ConfuserVersion.v17_r75056_dynamic:
				case ConfuserVersion.v17_r75056_native:
				case ConfuserVersion.v18_r75257_dynamic:
				case ConfuserVersion.v18_r75257_native:
					return FindKey4_other(method, out key);
				default:
					throw new ApplicationException("Invalid version");
				}
			}

			static bool FindKey4_normal(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					if (!instrs[i].IsLdloc())
						continue;
					if (!instrs[i + 1].IsLdloc())
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Add)
						continue;
					var ldci4 = instrs[i + 3];
					if (!ldci4.IsLdcI4())
						continue;
					if (instrs[i + 4].OpCode.Code != Code.Mul)
						continue;
					if (!instrs[i + 5].IsStloc())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}

			static bool FindKey4_other(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.IO.BinaryReader::ReadInt32()");
					if (index < 0)
						break;
					if (index + 1 >= instrs.Count)
						break;
					var ldci4 = instrs[index + 1];
					if (!ldci4.IsLdcI4())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}

			bool FindKey5(MethodDef method, out uint key) {
				switch (version) {
				case ConfuserVersion.v17_r74788_normal:
				case ConfuserVersion.v17_r74788_dynamic:
				case ConfuserVersion.v17_r74788_native:
				case ConfuserVersion.v17_r74816_normal:
				case ConfuserVersion.v17_r74816_dynamic:
				case ConfuserVersion.v17_r74816_native:
				case ConfuserVersion.v17_r75056_normal:
				case ConfuserVersion.v17_r75056_dynamic:
				case ConfuserVersion.v17_r75056_native:
				case ConfuserVersion.v18_r75257_normal:
				case ConfuserVersion.v18_r75257_dynamic:
				case ConfuserVersion.v18_r75257_native:
					return FindKey5_v17_r74788(method, out key);
				default:
					key = 0;
					return true;
				}
			}

			static bool FindKey5_v17_r74788(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.Assembly::GetModule(System.String)");
					if (i < 0)
						break;
					if (i + 1 >= instrs.Count)
						break;
					var ldci4 = instrs[i + 1];
					if (!ldci4.IsLdcI4())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}
		}

		public override bool Detected => initMethod != null;

		public ConstantsDecrypterV17(ModuleDefMD module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, fileData, simpleDeobfuscator) {
		}

		static readonly string[] requiredLocalsCctor = new string[] {
			"System.Reflection.Assembly",
			"System.IO.Compression.DeflateStream",
			"System.Byte[]",
			"System.Int32",
		};
		public void Find() {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;
			if (!new LocalTypes(cctor).All(requiredLocalsCctor))
				return;

			simpleDeobfuscator.Deobfuscate(cctor, SimpleDeobfuscatorFlags.Force | SimpleDeobfuscatorFlags.DisableConstantsFolderExtraInstrs);
			if (!Add(ConstantsDecrypterUtils.FindDictField(cctor, cctor.DeclaringType)))
				return;
			if (!Add(ConstantsDecrypterUtils.FindStreamField(cctor, cctor.DeclaringType)))
				return;

			var method = GetDecryptMethod();
			if (method == null)
				return;

			resourceName = GetResourceName(cctor);

			if (resourceName != null) {
				simpleDeobfuscator.Deobfuscate(method);
				keyArraySize = GetKeyArraySize(method);
				if (keyArraySize == 8)
					InitVersion(method, ConfuserVersion.v17_r75056_normal, ConfuserVersion.v17_r75056_dynamic, ConfuserVersion.v17_r75056_native);
				else if (keyArraySize == 16)
					InitVersion(method, ConfuserVersion.v18_r75257_normal, ConfuserVersion.v18_r75257_dynamic, ConfuserVersion.v18_r75257_native);
				else
					return;
			}
			else if (DotNetUtils.CallsMethod(method, "System.String System.Reflection.Module::get_ScopeName()"))
				InitVersion(method, ConfuserVersion.v17_r74816_normal, ConfuserVersion.v17_r74816_dynamic, ConfuserVersion.v17_r74816_native);
			else if (DotNetUtils.CallsMethod(method, "System.Reflection.Module System.Reflection.Assembly::GetModule(System.String)"))
				InitVersion(method, ConfuserVersion.v17_r74788_normal, ConfuserVersion.v17_r74788_dynamic, ConfuserVersion.v17_r74788_native);
			else
				InitVersion(method, ConfuserVersion.v17_r74708_normal, ConfuserVersion.v17_r74708_dynamic, ConfuserVersion.v17_r74708_native);

			initMethod = cctor;
		}

		void InitVersion(MethodDef method, ConfuserVersion normal, ConfuserVersion dynamic, ConfuserVersion native) {
			if (DeobUtils.HasInteger(method, 0x100) &&
				DeobUtils.HasInteger(method, 0x10000) &&
				DeobUtils.HasInteger(method, 0xFFFF))
				version = normal;
			else if ((nativeMethod = FindNativeMethod(method)) == null)
				version = dynamic;
			else
				version = native;
		}

		MethodDef GetDecryptMethod() {
			foreach (var type in module.Types) {
				if (type.Attributes != (TypeAttributes.Abstract | TypeAttributes.Sealed))
					continue;
				if (!CheckMethods(type.Methods))
					continue;
				foreach (var method in type.Methods) {
					if (!DotNetUtils.IsMethod(method, "System.Object", "(System.UInt32,System.UInt32)"))
						continue;

					return method;
				}
			}
			return null;
		}

		protected override byte[] DecryptData(DecrypterInfo info2, MethodDef caller, object[] args, out byte typeCode) {
			var info = (DecrypterInfoV17)info2;
			uint offs = info.CalcHash(info2.decryptMethod.MDToken.ToUInt32() ^ (info2.decryptMethod.DeclaringType.MDToken.ToUInt32() * (uint)args[0])) ^ (uint)args[1];
			reader.Position = offs;
			typeCode = reader.ReadByte();
			if (typeCode != info.int32Type && typeCode != info.int64Type &&
				typeCode != info.singleType && typeCode != info.doubleType &&
				typeCode != info.stringType)
				throw new ApplicationException("Invalid type code");

			var encrypted = reader.ReadBytes(reader.ReadInt32());
			return DecryptConstant(info, encrypted, offs, typeCode);
		}

		byte[] DecryptConstant(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) {
			switch (info.version) {
			case ConfuserVersion.v17_r74708_normal: return DecryptConstant_v17_r74708_normal(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74708_dynamic: return DecryptConstant_v17_r74708_dynamic(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74708_native: return DecryptConstant_v17_r74708_native(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74788_normal: return DecryptConstant_v17_r74788_normal(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74788_dynamic: return DecryptConstant_v17_r74788_dynamic(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74788_native: return DecryptConstant_v17_r74788_native(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74816_normal: return DecryptConstant_v17_r74788_normal(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74816_dynamic: return DecryptConstant_v17_r74788_dynamic(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r74816_native: return DecryptConstant_v17_r74788_native(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r75056_normal: return DecryptConstant_v17_r74788_normal(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r75056_dynamic: return DecryptConstant_v17_r74788_dynamic(info, encrypted, offs, typeCode);
			case ConfuserVersion.v17_r75056_native: return DecryptConstant_v17_r74788_native(info, encrypted, offs, typeCode);
			case ConfuserVersion.v18_r75257_normal: return DecryptConstant_v17_r74788_normal(info, encrypted, offs, typeCode);
			case ConfuserVersion.v18_r75257_dynamic: return DecryptConstant_v17_r74788_dynamic(info, encrypted, offs, typeCode);
			case ConfuserVersion.v18_r75257_native: return DecryptConstant_v17_r74788_native(info, encrypted, offs, typeCode);
			default:
				throw new ApplicationException("Invalid version");
			}
		}

		byte[] DecryptConstant_v17_r74708_normal(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			ConfuserUtils.Decrypt(info.key4 * (offs + typeCode), encrypted);

		byte[] DecryptConstant_v17_r74708_dynamic(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, info.key4);

		byte[] DecryptConstant_v17_r74708_native(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			DecryptConstant_v17_r73764_native(info, encrypted, offs, info.key4);

		byte[] DecryptConstant_v17_r74788_normal(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			ConfuserUtils.Decrypt(info.key4 * (offs + typeCode), encrypted, GetKey_v17_r74788(info));

		byte[] DecryptConstant_v17_r74788_dynamic(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, info.key4, GetKey_v17_r74788(info));

		byte[] DecryptConstant_v17_r74788_native(DecrypterInfoV17 info, byte[] encrypted, uint offs, byte typeCode) =>
			DecryptConstant_v17_r73764_native(info, encrypted, offs, info.key4, GetKey_v17_r74788(info));

		byte[] GetKey_v17_r74788(DecrypterInfoV17 info) {
			var key = module.ReadBlob(info.decryptMethod.MDToken.ToUInt32() ^ info.key5);
			if (key.Length != keyArraySize)
				throw new ApplicationException("Invalid key size");
			return key;
		}

		public override void Initialize() {
			if (resourceName != null)
				resource = DotNetUtils.GetResource(module, resourceName) as EmbeddedResource;
			else
				resource = FindResource(initMethod);
			if (resource == null)
				throw new ApplicationException("Could not find encrypted consts resource");

			FindDecrypterInfos();
			InitializeDecrypterInfos();

			SetConstantsData(DeobUtils.Inflate(resource.CreateReader().ToArray(), true));
		}

		void FindDecrypterInfos() {
			foreach (var type in module.Types) {
				if (type.Attributes != (TypeAttributes.Abstract | TypeAttributes.Sealed))
					continue;
				if (!CheckMethods(type.Methods))
					continue;
				foreach (var method in type.Methods) {
					if (!DotNetUtils.IsMethod(method, "System.Object", "(System.UInt32,System.UInt32)"))
						continue;

					var info = new DecrypterInfoV17(version, method);
					Add(info);
				}
			}
		}

		static bool CheckMethods(IEnumerable<MethodDef> methods) {
			int numMethods = 0;
			foreach (var method in methods) {
				if (method.Name == ".ctor" || method.Name == ".cctor")
					return false;
				if (method.Attributes != (MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.CompilerControlled))
					return false;
				if (!DotNetUtils.IsMethod(method, "System.Object", "(System.UInt32,System.UInt32)"))
					return false;

				numMethods++;
			}
			return numMethods > 0;
		}

		static string GetResourceName(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Call, "System.Byte[] System.BitConverter::GetBytes(System.Int32)");
				if (i < 0)
					break;
				if (i == 0)
					continue;
				var ldci4 = instrs[i - 1];
				if (!ldci4.IsLdcI4())
					continue;
				return Encoding.UTF8.GetString(BitConverter.GetBytes(ldci4.GetLdcI4Value()));
			}
			return null;
		}

		static int GetKeyArraySize(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (!instrs[i].IsLdloc())
					continue;
				if (!instrs[i + 1].IsLdloc())
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Rem)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Ldelem_U1)
					continue;

				return ldci4.GetLdcI4Value();
			}
			return -1;
		}

		public override bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v17_r74708_normal:
			case ConfuserVersion.v17_r74708_dynamic:
			case ConfuserVersion.v17_r74708_native:
				minRev = 74708;
				maxRev = 74708;
				return true;

			case ConfuserVersion.v17_r74788_normal:
			case ConfuserVersion.v17_r74788_dynamic:
			case ConfuserVersion.v17_r74788_native:
				minRev = 74788;
				maxRev = 74788;
				return true;

			case ConfuserVersion.v17_r74816_normal:
			case ConfuserVersion.v17_r74816_dynamic:
			case ConfuserVersion.v17_r74816_native:
				minRev = 74816;
				maxRev = 74852;
				return true;

			case ConfuserVersion.v17_r75056_normal:
			case ConfuserVersion.v17_r75056_dynamic:
			case ConfuserVersion.v17_r75056_native:
				minRev = 75056;
				maxRev = 75184;
				return true;

			case ConfuserVersion.v18_r75257_normal:
			case ConfuserVersion.v18_r75257_dynamic:
			case ConfuserVersion.v18_r75257_native:
				minRev = 75257;
				maxRev = 75349;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
