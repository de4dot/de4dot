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
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	// Since v1.8 r75367
	class ConstantsDecrypterV18 : IVersionProvider {
		ModuleDefMD module;
		byte[] fileData;
		ISimpleDeobfuscator simpleDeobfuscator;
		FieldDef dictField, dataField;
		MethodDef installMethod;
		MethodDefAndDeclaringTypeDict<DecrypterInfo> decrypters = new MethodDefAndDeclaringTypeDict<DecrypterInfo>();
		uint key0, key0d;
		MethodDef nativeMethod;
		EmbeddedResource resource;
		byte[] constants;
		ConfuserVersion version = ConfuserVersion.Unknown;

		public enum ConfuserVersion {
			Unknown,
			v18_r75367_normal,
			v18_r75367_dynamic,
			v18_r75367_native,
			v18_r75369_normal,
			v18_r75369_dynamic,
			v18_r75369_native,
		}

		public class DecrypterInfo {
			readonly ConstantsDecrypterV18 constantsDecrypter;
			public readonly MethodDef method;
			public ulong key0l, key1l, key2l;
			public uint key0, key0d;
			readonly ConfuserVersion version;

			public DecrypterInfo(ConstantsDecrypterV18 constantsDecrypter, MethodDef method, ConfuserVersion version) {
				this.constantsDecrypter = constantsDecrypter;
				this.method = method;
				this.version = version;
			}

			public string decryptString(uint magic1, ulong magic2) {
				return Encoding.UTF8.GetString(decrypt(magic1, magic2));
			}

			public int decryptInt32(uint magic1, ulong magic2) {
				return BitConverter.ToInt32(decrypt(magic1, magic2), 0);
			}

			public long decryptInt64(uint magic1, ulong magic2) {
				return BitConverter.ToInt64(decrypt(magic1, magic2), 0);
			}

			public float decryptSingle(uint magic1, ulong magic2) {
				return BitConverter.ToSingle(decrypt(magic1, magic2), 0);
			}

			public double decryptDouble(uint magic1, ulong magic2) {
				return BitConverter.ToDouble(decrypt(magic1, magic2), 0);
			}

			byte[] decrypt(uint magic1, ulong magic2) {
				ulong info = hash(method.DeclaringType.MDToken.ToUInt32() * magic1) ^ magic2;
				int offset = (int)(info >> 32);
				int len = (int)info;
				var decrypted = new byte[len];
				byte[] key = BitConverter.GetBytes(method.MDToken.ToUInt32() ^ key0d);
				for (int i = 0; i < len; i++)
					decrypted[i] = (byte)(constantsDecrypter.constants[offset + i] ^ key[(offset + i) & 3]);
				return decrypted;
			}

			ulong hash(uint magic) {
				switch (version) {
				case ConfuserVersion.v18_r75367_normal:
				case ConfuserVersion.v18_r75367_dynamic:
				case ConfuserVersion.v18_r75367_native:
					return hash1(key0l ^ magic);
				case ConfuserVersion.v18_r75369_normal:
				case ConfuserVersion.v18_r75369_dynamic:
				case ConfuserVersion.v18_r75369_native:
					return hash1(key0l * magic);
				default:
					throw new ApplicationException("Invalid version");
				}
			}

			ulong hash1(ulong h0) {
				ulong h1 = key1l;
				ulong h2 = key2l;
				h1 *= h0;
				h2 *= h0;
				h0 *= h0;
				ulong hash = 14695981039346656037UL;
				while (h0 != 0) {
					hash *= 1099511628211UL;
					hash = (hash ^ h0) + (h1 ^ h2) * key0;
					h1 *= 0x811C9DC5;
					h2 *= 0xA2CEBAB2;
					h0 >>= 8;
				}
				return hash;
			}
		}

		public IEnumerable<TypeDef> Types {
			get {
				var types = new List<TypeDef>();
				foreach (var info in decrypters.getValues())
					types.Add(info.method.DeclaringType);
				return types;
			}
		}

		public IEnumerable<FieldDef> Fields {
			get {
				return new List<FieldDef> {
					dataField,
					dictField,
				};
			}
		}

		public MethodDef NativeMethod {
			get { return nativeMethod; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public IEnumerable<DecrypterInfo> Decrypters {
			get { return decrypters.getValues(); }
		}

		public bool Detected {
			get { return installMethod != null; }
		}

		public ConstantsDecrypterV18(ModuleDefMD module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void find() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;
			simpleDeobfuscator.deobfuscate(cctor, true);

			if ((dictField = ConstantsDecrypterUtils.findDictField(cctor, cctor.DeclaringType)) == null)
				return;
			if ((dataField = ConstantsDecrypterUtils.findDataField(cctor, cctor.DeclaringType)) == null)
				return;

			nativeMethod = findNativeMethod(cctor, cctor.DeclaringType);

			var method = getDecryptMethod();
			if (method == null)
				return;
			simpleDeobfuscator.deobfuscate(method);
			var info = new DecrypterInfo(this, method, ConfuserVersion.Unknown);
			if (findKeys_v18_r75367(info))
				initVersion(cctor, ConfuserVersion.v18_r75367_normal, ConfuserVersion.v18_r75367_dynamic, ConfuserVersion.v18_r75367_native);
			else if (findKeys_v18_r75369(info))
				initVersion(cctor, ConfuserVersion.v18_r75369_normal, ConfuserVersion.v18_r75369_dynamic, ConfuserVersion.v18_r75369_native);
			else
				return;

			installMethod = cctor;
		}

		void initVersion(MethodDef installMethod, ConfuserVersion normal, ConfuserVersion dynamic, ConfuserVersion native) {
			if (nativeMethod != null)
				version = native;
			else if (DeobUtils.hasInteger(installMethod, 0x10000))
				version = normal;
			else
				version = dynamic;
		}

		public void initialize() {
			if (installMethod == null)
				return;

			if (!findKeys())
				throw new ApplicationException("Could not find keys");

			if ((resource = findResource(key0)) == null)
				throw new ApplicationException("Could not find resource");
			constants = decryptResource(resource.GetResourceData());

			findDecrypters();
		}

		EmbeddedResource findResource(uint magic) {
			var name = Encoding.UTF8.GetString(BitConverter.GetBytes(magic));
			return DotNetUtils.getResource(module, name) as EmbeddedResource;
		}

		bool findKeys() {
			if (!findKey0(installMethod, out key0))
				return false;
			if (!findKey0d(installMethod, out key0d))
				return false;

			return true;
		}

		static bool findKey0(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Text.Encoding System.Text.Encoding::get_UTF8()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Byte[] System.BitConverter::GetBytes(System.Int32)");
				if (index2 - index != 2)
					continue;
				var ldci4 = instrs[index + 1];
				if (!ldci4.IsLdcI4())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		static bool findKey0d(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (!instrs[index + 2].IsLdloc())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		static MethodDef findNativeMethod(MethodDef method, TypeDef declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				if (!instrs[i].IsLdloc())
					continue;
				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || !calledMethod.IsNative)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int32", "(System.Int32)"))
					continue;

				return calledMethod;
			}
			return null;
		}

		MethodDef getDecryptMethod() {
			foreach (var type in module.Types) {
				if (type.Attributes != (TypeAttributes.Abstract | TypeAttributes.Sealed))
					continue;
				if (!checkMethods(type.Methods))
					continue;
				foreach (var method in type.Methods) {
					if (isDecryptMethodSignature(method))
						return method;
				}
			}
			return null;
		}

		static bool checkMethods(IEnumerable<MethodDef> methods) {
			int numMethods = 0;
			foreach (var method in methods) {
				if (method.Name == ".ctor" || method.Name == ".cctor")
					return false;
				if (!isDecryptMethodSignature(method))
					return false;

				numMethods++;
			}
			return numMethods > 0;
		}

		static bool isDecryptMethodSignature(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			if (method.Attributes != (MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.CompilerControlled))
				return false;
			var sig = method.MethodSig;
			if (sig == null)
				return false;
			if (sig.Params.Count != 2)
				return false;
			if (sig.Params[0].GetElementType() != ElementType.U4)
				return false;
			if (sig.Params[1].GetElementType() != ElementType.U8)
				return false;
			if (!(sig.RetType.RemovePinnedAndModifiers() is GenericMVar))
				return false;
			if (sig.GenParamCount != 1)
				return false;
			return true;
		}

		void findDecrypters() {
			foreach (var type in module.Types) {
				if (type.Attributes != (TypeAttributes.Abstract | TypeAttributes.Sealed))
					continue;
				if (!checkMethods(type.Methods))
					continue;
				foreach (var method in type.Methods) {
					var info = createDecrypterInfo(method);
					if (info != null)
						decrypters.add(info.method, info);
				}
			}
		}

		DecrypterInfo createDecrypterInfo(MethodDef method) {
			if (!isDecryptMethodSignature(method))
				return null;

			simpleDeobfuscator.deobfuscate(method);
			var info = new DecrypterInfo(this, method, version);
			if (!findKeys(info))
				return null;

			return info;
		}

		bool findKeys(DecrypterInfo info) {
			switch (version) {
			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v18_r75367_dynamic:
			case ConfuserVersion.v18_r75367_native:
				return findKeys_v18_r75367(info);
			case ConfuserVersion.v18_r75369_normal:
			case ConfuserVersion.v18_r75369_dynamic:
			case ConfuserVersion.v18_r75369_native:
				return findKeys_v18_r75369(info);
			default:
				throw new ApplicationException("Invalid version");
			}
		}

		static bool findKeys_v18_r75367(DecrypterInfo info) {
			if (!findLKeys_v18_r75367(info))
				return false;
			if (!findKey0_v18_r75367(info))
				return false;
			if (!findKey0d_v18_r75367(info))
				return false;
			return true;
		}

		static bool findKeys_v18_r75369(DecrypterInfo info) {
			if (!findLKeys_v18_r75369(info))
				return false;
			if (!findKey0_v18_r75369(info))
				return false;
			if (!findKey0d_v18_r75367(info))
				return false;
			return true;
		}

		static bool findLKeys_v18_r75367(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 10; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (!instrs[i + 1].IsLdloc())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Conv_U8)
					continue;
				if (!instrs[i + 4].IsStloc())
					continue;
				var ldci4_2 = instrs[i + 5];
				if (!ldci4_2.IsLdcI4())
					continue;
				if (instrs[i + 6].OpCode.Code != Code.Conv_I8)
					continue;
				if (!instrs[i + 7].IsStloc())
					continue;
				var ldci4_3 = instrs[i + 8];
				if (!ldci4_3.IsLdcI4())
					continue;
				if (instrs[i + 9].OpCode.Code != Code.Conv_I8)
					continue;
				if (!instrs[i + 10].IsStloc())
					continue;

				info.key0l = (uint)ldci4_1.GetLdcI4Value();
				info.key1l = (uint)ldci4_2.GetLdcI4Value();
				info.key2l = (uint)ldci4_3.GetLdcI4Value();
				return true;
			}
			return false;
		}

		static bool findKey0_v18_r75367(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (instrs[i].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Conv_I8)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Mul)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Add)
					continue;

				info.key0 = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			return false;
		}

		static bool findKey0d_v18_r75367(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, index, Code.Call, "System.Byte[] System.BitConverter::GetBytes(System.Int32)");
				if (index2 < 0)
					break;
				if (index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				info.key0d = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			return false;
		}

		static bool findLKeys_v18_r75369(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 8; i++) {
				var ldci8_1 = instrs[i];
				if (ldci8_1.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!instrs[i + 1].IsLdloc())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Conv_U8)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Mul)
					continue;
				if (!instrs[i + 4].IsStloc())
					continue;
				var ldci8_2 = instrs[i + 5];
				if (ldci8_2.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!instrs[i + 6].IsStloc())
					continue;
				var ldci8_3 = instrs[i + 7];
				if (ldci8_3.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!instrs[i + 8].IsStloc())
					continue;

				info.key0l = (ulong)(long)ldci8_1.Operand;
				info.key1l = (ulong)(long)ldci8_2.Operand;
				info.key2l = (ulong)(long)ldci8_3.Operand;
				return true;
			}
			return false;
		}

		static bool findKey0_v18_r75369(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (!instrs[i].IsLdloc())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Conv_U8)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Mul)
					continue;

				info.key0 = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			return false;
		}

		byte[] decryptResource(byte[] encrypted) {
			switch (version) {
			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v18_r75369_normal:
				return decryptResource_v18_r75367_normal(encrypted);

			case ConfuserVersion.v18_r75367_dynamic:
			case ConfuserVersion.v18_r75369_dynamic:
				return decryptResource_v18_r75367_dynamic(encrypted);

			case ConfuserVersion.v18_r75367_native:
			case ConfuserVersion.v18_r75369_native:
				return decryptResource_v18_r75367_native(encrypted);

			default:
				throw new ApplicationException("Unknown version");
			}
		}

		byte[] getSigKey() {
			return module.ReadBlob(key0d ^ installMethod.MDToken.ToUInt32());
		}

		byte[] decryptResource_v18_r75367_normal(byte[] encrypted) {
			var key = getSigKey();
			var decrypted = ConfuserUtils.decrypt(BitConverter.ToUInt32(key, 12) * (uint)key0, encrypted);
			return DeobUtils.inflate(DeobUtils.aesDecrypt(decrypted, key, DeobUtils.md5Sum(key)), true);
		}

		static int getDynamicStartIndex(IList<Instruction> instrs, int ldlocIndex) {
			for (int i = ldlocIndex - 1; i >= 0; i--) {
				if (instrs[i].OpCode.FlowControl != FlowControl.Next)
					return i + 1;
			}
			return 0;
		}

		int getDynamicEndIndex(int startIndex, Local local) {
			if (startIndex < 0)
				return -1;
			var instrs = installMethod.Body.Instructions;
			for (int i = startIndex; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.IsStloc() && instr.GetLocal(installMethod.Body.Variables) == local)
					return i;
			}
			return -1;
		}

		Local getDynamicLocal(out int instrIndex) {
			var instrs = installMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Void System.IO.BinaryWriter::Write(System.Byte)");
				if (i < 0)
					break;
				int index = i - 2;
				if (index < 0)
					continue;
				var ldloc = instrs[index];
				if (!ldloc.IsLdloc())
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Conv_U1)
					continue;

				instrIndex = index;
				return ldloc.GetLocal(installMethod.Body.Variables);
			}
			instrIndex = 0;
			return null;
		}

		byte[] decryptResource_v18_r75367_dynamic(byte[] encrypted) {
			int ldlocIndex;
			var local = getDynamicLocal(out ldlocIndex);
			if (local == null)
				throw new ApplicationException("Could not find local");

			var instrs = installMethod.Body.Instructions;
			int startIndex = getDynamicStartIndex(instrs, ldlocIndex);
			int endIndex = getDynamicEndIndex(startIndex, local);
			if (endIndex < 0)
				throw new ApplicationException("Could not find endIndex");

			var constReader = new ConstantsReader(installMethod);

			return decryptResource(encrypted, magic => {
				constReader.setConstantInt32(local, magic);
				int index = startIndex, result;
				if (!constReader.getNextInt32(ref index, out result))
					throw new ApplicationException("Could not get constant");
				if (index != endIndex)
					throw new ApplicationException("Wrong constant");
				return (byte)result;
			});
		}

		byte[] decryptResource_v18_r75367_native(byte[] encrypted) {
			using (var x86Emu = new x86Emulator(fileData))
				return decryptResource(encrypted, magic => (byte)x86Emu.emulate((uint)nativeMethod.RVA, magic));
		}

		byte[] decryptResource(byte[] encrypted, Func<uint, byte> decryptFunc) {
			var key = getSigKey();

			var decrypted = DeobUtils.aesDecrypt(encrypted, key, DeobUtils.md5Sum(key));
			decrypted = DeobUtils.inflate(decrypted, true);

			var reader = MemoryImageStream.Create(decrypted);
			var result = new MemoryStream();
			var writer = new BinaryWriter(result);
			while (reader.Position < reader.Length) {
				uint magic = reader.Read7BitEncodedUInt32();
				writer.Write(decryptFunc(magic));
			}

			return result.ToArray();
		}

		static bool verifyGenericArg(MethodSpec gim, ElementType etype) {
			if (gim == null)
				return false;
			var gims = gim.GenericInstMethodSig;
			if (gims == null || gims.GenericArguments.Count != 1)
				return false;
			return gims.GenericArguments[0].GetElementType() == etype;
		}

		public string decryptString(MethodDef method, MethodSpec gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.String))
				return null;
			var info = decrypters.find(method);
			return info.decryptString(magic1, magic2);
		}

		public object decryptInt32(MethodDef method, MethodSpec gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.I4))
				return null;
			var info = decrypters.find(method);
			return info.decryptInt32(magic1, magic2);
		}

		public object decryptInt64(MethodDef method, MethodSpec gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.I8))
				return null;
			var info = decrypters.find(method);
			return info.decryptInt64(magic1, magic2);
		}

		public object decryptSingle(MethodDef method, MethodSpec gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.R4))
				return null;
			var info = decrypters.find(method);
			return info.decryptSingle(magic1, magic2);
		}

		public object decryptDouble(MethodDef method, MethodSpec gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.R8))
				return null;
			var info = decrypters.find(method);
			return info.decryptDouble(magic1, magic2);
		}

		public void cleanUp() {
			if (installMethod == null)
				return;

			//TODO: Only remove its code
			installMethod.Body.Instructions.Clear();
			installMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
		}

		public bool getRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v18_r75367_dynamic:
			case ConfuserVersion.v18_r75367_native:
				minRev = 75367;
				maxRev = 75367;
				return true;

			case ConfuserVersion.v18_r75369_normal:
			case ConfuserVersion.v18_r75369_dynamic:
			case ConfuserVersion.v18_r75369_native:
				minRev = 75369;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
