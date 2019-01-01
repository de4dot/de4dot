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
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class AssemblyResolver : ResolverBase {
		Version version;
		List<FieldInfo> fieldInfos;
		MethodDef decryptMethod;

		enum Version {
			Unknown,
			V3Old,
			V3,
			V4,
			V404,
			V41,
			V41SL,
		}

		public class AssemblyInfo {
			public byte[] data;
			public string fullName;
			public string simpleName;
			public string extension;
			public EmbeddedResource resource;

			public AssemblyInfo(byte[] data, string fullName, string simpleName, string extension, EmbeddedResource resource) {
				this.data = data;
				this.fullName = fullName;
				this.simpleName = simpleName;
				this.extension = extension;
				this.resource = resource;
			}

			public override string ToString() => fullName;
		}

		class FieldInfo {
			public FieldDef field;
			public int magic;

			public FieldInfo(FieldDef field, int magic) {
				this.field = field;
				this.magic = magic;
			}
		}

		public MethodDef DecryptMethod => decryptMethod;

		public AssemblyResolver(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		static string[] requiredLocals_sl = new string[] {
			"System.Byte[]",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.Security.Cryptography.SHA1Managed",
			"System.Windows.AssemblyPart",
		};
		protected override bool CheckResolverInitMethodSilverlight(MethodDef resolverInitMethod) {
			if (resolverInitMethod.Body.ExceptionHandlers.Count != 1)
				return false;

			foreach (var method in DotNetUtils.GetCalledMethods(module, resolverInitMethod)) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!method.IsPublic || method.HasGenericParameters)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.String)"))
					continue;
				simpleDeobfuscator.Deobfuscate(method);
				if (!new LocalTypes(method).All(requiredLocals_sl))
					continue;

				initMethod = resolverInitMethod;
				resolveHandler = method;
				UpdateVersion(resolveHandler);
				return true;
			}

			return false;
		}

		void UpdateVersion(MethodDef handler) {
			if (IsV3Old(handler)) {
				version = Version.V3Old;
				return;
			}
			if (IsV3SL(handler)) {
				version = Version.V3;	// 3.x-4.0.4
				return;
			}
			if (IsV41SL(handler)) {
				version = Version.V41SL;
				return;
			}
		}

		static bool IsV3SL(MethodDef handler) {
			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				if (!instrs[i].IsLdloc())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Add)
					continue;
				if (!instrs[i + 2].IsLdcI4())
					continue;
				if (instrs[i + 3].OpCode.Code != Code.And)
					continue;
				return true;
			}
			return false;
		}

		static bool IsV41SL(MethodDef handler) {
			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				if (!instrs[i].IsLdcI4() || instrs[i].GetLdcI4Value() != 5)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.And)
					continue;
				if (!instrs[i + 2].IsLdcI4() || instrs[i + 2].GetLdcI4Value() != 0x1F)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.And)
					continue;
				return true;
			}
			return false;
		}

		static bool IsV3Old(MethodDef method) =>
			DotNetUtils.CallsMethod(method, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)") &&
			!DotNetUtils.CallsMethod(method, "System.Int32 System.IO.Stream::ReadByte()") &&
			// Obfuscated System.Int32 System.IO.Stream::ReadByte()
			!DotNetUtils.CallsMethod(method, "System.Int32", "(System.IO.Stream,System.Int32,System.Int32)");

		protected override bool CheckResolverInitMethodInternal(MethodDef resolverInitMethod) =>
			DotNetUtils.CallsMethod(resolverInitMethod, "System.Void System.AppDomain::add_AssemblyResolve(System.ResolveEventHandler)");

		protected override bool CheckHandlerMethodDesktopInternal(MethodDef handler) {
			if (CheckHandlerV3(handler) || CheckHandlerSL(handler)) {
				UpdateVersion(handler);
				return true;
			}

			simpleDeobfuscator.Deobfuscate(handler);
			if (CheckHandlerV4(handler, out var fieldInfosTmp, out var decryptMethodTmp)) {
				version = Version.V4;
				fieldInfos = fieldInfosTmp;
				decryptMethod = decryptMethodTmp;
				return true;
			}

			var versionTmp = CheckHandlerV404_41(handler, out fieldInfosTmp, out decryptMethodTmp);
			if (fieldInfosTmp.Count != 0) {
				version = versionTmp;
				fieldInfos = fieldInfosTmp;
				decryptMethod = decryptMethodTmp;
				return true;
			}

			return false;
		}

		static string[] handlerLocalTypes_NET = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.Security.Cryptography.SHA1CryptoServiceProvider",
			"System.String",
		};
		static bool CheckHandlerV3(MethodDef handler) => new LocalTypes(handler).All(handlerLocalTypes_NET);

		static string[] handlerLocalTypes_SL = new string[] {
			"System.Byte[]",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.Security.Cryptography.SHA1Managed",
			"System.String",
			"System.Windows.AssemblyPart",
		};
		static bool CheckHandlerSL(MethodDef handler) => new LocalTypes(handler).All(handlerLocalTypes_SL);

		// 4.0.1.18 .. 4.0.3
		bool CheckHandlerV4(MethodDef handler, out List<FieldInfo> fieldInfos, out MethodDef decryptMethod) {
			fieldInfos = new List<FieldInfo>();
			decryptMethod = null;

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = i;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDef;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					return false;

				var ldci4_len = instrs[index++];
				if (!ldci4_len.IsLdcI4())
					return false;
				if (ldci4_len.GetLdcI4Value() != field.InitialValue.Length)
					return false;

				var ldci4_magic = instrs[index++];
				if (!ldci4_magic.IsLdcI4())
					return false;
				int magic = ldci4_magic.GetLdcI4Value();

				var call = instrs[index++];
				if (call.OpCode.Code == Code.Tailcall)
					call = instrs[index++];
				if (call.OpCode.Code != Code.Call)
					return false;
				var decryptMethodTmp = call.Operand as MethodDef;
				if (!DotNetUtils.IsMethod(decryptMethodTmp, "System.Reflection.Assembly", "(System.RuntimeFieldHandle,System.Int32,System.Int32)"))
					return false;

				decryptMethod = decryptMethodTmp;
				fieldInfos.Add(new FieldInfo(field, magic));
			}

			return fieldInfos.Count != 0;
		}

		// 4.0.4, 4.1+
		Version CheckHandlerV404_41(MethodDef handler, out List<FieldInfo> fieldInfos, out MethodDef decryptMethod) {
			var version = Version.Unknown;
			fieldInfos = new List<FieldInfo>();
			decryptMethod = null;

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count - 6; i++) {
				int index = i;

				var ldci4_len = instrs[index++];
				if (!ldci4_len.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Newarr)
					continue;
				if (!instrs[index++].IsStloc())
					continue;
				if (!instrs[index++].IsLdloc())
					continue;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDef;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				var call1 = instrs[index++];
				if (call1.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.IsMethod(call1.Operand as IMethod, "System.Void", "(System.Array,System.RuntimeFieldHandle)"))
					continue;

				int callIndex = GetCallDecryptMethodIndex(instrs, index);
				if (callIndex < 0)
					continue;
				var args = DsUtils.GetArgValues(instrs, callIndex);
				if (args == null)
					continue;
				var decryptMethodTmp = instrs[callIndex].Operand as MethodDef;
				if (decryptMethodTmp == null)
					continue;
				GetMagic(decryptMethodTmp, args, out var versionTmp, out int magic);

				version = versionTmp;
				decryptMethod = decryptMethodTmp;
				fieldInfos.Add(new FieldInfo(field, magic));
			}

			return version;
		}

		static bool GetMagic(MethodDef method, IList<object> args, out Version version, out int magic) {
			magic = 0;
			int magicIndex = GetMagicIndex(method, out version);
			if (magicIndex < 0 || magicIndex >= args.Count)
				return false;
			var val = args[magicIndex];
			if (!(val is int))
				return false;

			magic = (int)val;
			return true;
		}

		static int GetMagicIndex(MethodDef method, out Version version) {
			int magicIndex = GetMagicIndex404(method);
			if (magicIndex >= 0) {
				version = Version.V404;
				return magicIndex;
			}

			magicIndex = GetMagicIndex41Trial(method);
			if (magicIndex >= 0) {
				version = Version.V41;
				return magicIndex;
			}

			version = Version.Unknown;
			return -1;
		}

		static int GetMagicIndex404(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;
				if (!instrs[index++].IsLdloc())
					continue;
				var ldarg = instrs[index++];
				if (!ldarg.IsLdarg())
					continue;
				if (instrs[index++].OpCode.Code != Code.Add)
					continue;
				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;
				if (ldci4.GetLdcI4Value() != 0xFF)
					continue;
				return ldarg.GetParameterIndex();
			}
			return -1;
		}

		static int GetMagicIndex41Trial(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;
				if (instrs[index++].OpCode.Code != Code.Div)
					continue;
				var ldarg = instrs[index++];
				if (!ldarg.IsLdarg())
					continue;
				if (instrs[index++].OpCode.Code != Code.Add)
					continue;
				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;
				if (ldci4.GetLdcI4Value() != 0xFF)
					continue;
				return ldarg.GetParameterIndex();
			}
			return -1;
		}

		static int GetCallDecryptMethodIndex(IList<Instruction> instrs, int index) {
			index = GetRetIndex(instrs, index);
			if (index < 0)
				return -1;
			for (int i = index - 1; i >= 0; i--) {
				var instr = instrs[i];
				if (!IsCallOrNext(instr))
					break;
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null || calledMethod.MethodSig.GetParamCount() < 2)
					continue;

				return i;
			}
			return -1;
		}

		static int GetRetIndex(IList<Instruction> instrs, int index) {
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Ret)
					return i;
				if (!IsCallOrNext(instr))
					break;
			}
			return -1;
		}

		static bool IsCallOrNext(Instruction instr) {
			switch (instr.OpCode.FlowControl) {
			case FlowControl.Call:
			case FlowControl.Next:
				return true;
			default:
				return false;
			}
		}

		public IEnumerable<AssemblyInfo> GetAssemblyInfos() {
			if (!Detected)
				return new List<AssemblyInfo>();

			switch (version) {
			case Version.V3Old:
				return GetAssemblyInfos(resource => DecryptResourceV3Old(resource));
			case Version.V3:
				return GetAssemblyInfos(resource => DecryptResourceV3(resource));
			case Version.V4:
			case Version.V404:
				return GetAssemblyInfosV4();
			case Version.V41:
				return GetAssemblyInfosV41();
			case Version.V41SL:
				return GetAssemblyInfos(resource => DecryptResourceV41SL(resource));
			default:
				throw new ApplicationException("Unknown version");
			}
		}

		IEnumerable<AssemblyInfo> GetAssemblyInfos(Func<EmbeddedResource, byte[]> decrypter) {
			var infos = new List<AssemblyInfo>();

			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Regex.IsMatch(resource.Name.String, @"^[0-9A-F]{40}$"))
					continue;
				var info = GetAssemblyInfo(resource, decrypter);
				if (info == null)
					continue;
				infos.Add(info);
			}

			return infos;
		}

		AssemblyInfo GetAssemblyInfo(EmbeddedResource resource, Func<EmbeddedResource, byte[]> decrypter) {
			try {
				var decrypted = decrypter(resource);
				return GetAssemblyInfo(decrypted, resource);
			}
			catch (Exception) {
				return null;
			}
		}

		AssemblyInfo GetAssemblyInfo(byte[] decryptedData, EmbeddedResource resource) {
			var asm = AssemblyDef.Load(decryptedData);
			var fullName = asm.FullName;
			var simpleName = asm.Name.String;
			var extension = DeobUtils.GetExtension(asm.Modules[0].Kind);
			return new AssemblyInfo(decryptedData, fullName, simpleName, extension, resource);
		}

		IEnumerable<AssemblyInfo> GetAssemblyInfos(Func<byte[], int, byte[]> decrypter) {
			var infos = new List<AssemblyInfo>();

			if (fieldInfos == null)
				return infos;

			foreach (var fieldInfo in fieldInfos) {
				var decrypted = decrypter(fieldInfo.field.InitialValue, fieldInfo.magic);
				infos.Add(GetAssemblyInfo(decrypted, null));
				fieldInfo.field.InitialValue = new byte[1];
				fieldInfo.field.FieldSig.Type = module.CorLibTypes.Byte;
				fieldInfo.field.RVA = 0;
			}

			return infos;
		}

		IEnumerable<AssemblyInfo> GetAssemblyInfosV4() => GetAssemblyInfos((data, magic) => DecryptResourceV4(data, magic));
		IEnumerable<AssemblyInfo> GetAssemblyInfosV41() => GetAssemblyInfos((data, magic) => InflateIfNeeded(Decrypt41Trial(data, magic)));

		static byte[] Decrypt41Trial(byte[] data, int magic) {
			for (int i = 0; i < data.Length; i++)
				data[i] ^= (byte)(i / 3 + magic);
			return data;
		}
	}
}
