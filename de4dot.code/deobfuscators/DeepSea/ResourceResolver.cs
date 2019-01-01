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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class ResourceResolver : ResolverBase {
		Data30 data30;
		Data40 data40;
		Data41 data41;
		ResourceVersion version = ResourceVersion.Unknown;

		enum ResourceVersion {
			Unknown,
			V3,
			V40,
			V41,
		}

		class Data30 {
			public EmbeddedResource resource;
		}

		class Data40 {
			public FieldDef resourceField;
			public MethodDef resolveHandler2;
			public MethodDef getDataMethod;
			public int magic;
		}

		class Data41 {
			public FieldDef resourceField;
			public MethodDef resolveHandler2;
			public int magic;
			public bool isTrial;
		}

		class HandlerInfo {
			public MethodDef handler;
			public IList<object> args;

			public HandlerInfo(MethodDef handler, IList<object> args) {
				this.handler = handler;
				this.args = args;
			}
		}

		public MethodDef InitMethod2 {
			get {
				if (data40 != null)
					return data40.resolveHandler2;
				if (data41 != null)
					return data41.resolveHandler2;
				return null;
			}
		}

		public MethodDef GetDataMethod => data40?.getDataMethod;
		public EmbeddedResource Resource => data30?.resource;

		public ResourceResolver(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		protected override bool CheckResolverInitMethodInternal(MethodDef resolverInitMethod) =>
			DotNetUtils.CallsMethod(resolverInitMethod, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)");

		protected override bool CheckHandlerMethodDesktopInternal(MethodDef handler) {
			if (CheckHandlerV3(handler)) {
				version = ResourceVersion.V3;
				return true;
			}

			simpleDeobfuscator.Deobfuscate(handler);
			if ((data40 = CheckHandlerV40(handler)) != null) {
				version = ResourceVersion.V40;
				return true;
			}

			var info = GetHandlerArgs41(handler);
			if (info != null && CheckHandlerV41(info, out var data41Tmp)) {
				version = ResourceVersion.V41;
				data41 = data41Tmp;
				return true;
			}

			return false;
		}

		HandlerInfo GetHandlerArgs41(MethodDef handler) {
			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (GetLdtokenField(calledMethod) == null)
					continue;
				var args = DsUtils.GetArgValues(instrs, i);
				if (args == null)
					continue;

				return new HandlerInfo(calledMethod, args);
			}
			return null;
		}

		bool CheckHandlerV41(HandlerInfo info, out Data41 data41) {
			data41 = new Data41();
			data41.resolveHandler2 = info.handler;
			data41.resourceField = GetLdtokenField(info.handler);
			if (data41.resourceField == null)
				return false;
			int magicArgIndex = GetMagicArgIndex41Retail(info.handler, out bool isOtherRetail);
			if (magicArgIndex < 0) {
				magicArgIndex = GetMagicArgIndex41Trial(info.handler);
				data41.isTrial = true;
			}
			var asmVer = module.Assembly.Version;
			if (magicArgIndex < 0 || magicArgIndex >= info.args.Count)
				return false;
			var val = info.args[magicArgIndex];
			if (!(val is int))
				return false;
			if (data41.isTrial)
				data41.magic = (int)val >> 3;
			else if (isOtherRetail)
				data41.magic = data41.resourceField.InitialValue.Length - (int)val;
			else
				data41.magic = ((asmVer.Major << 3) | (asmVer.Minor << 2) | asmVer.Revision) - (int)val;
			return true;
		}

		static int GetMagicArgIndex41Retail(MethodDef method, out bool isOtherRetail) {
			isOtherRetail = false;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				isOtherRetail = false;
				var ld = instrs[i];
				if (ld.IsLdarg())
					isOtherRetail = true;
				else if (!ld.IsLdloc())
					continue;

				var add = instrs[i + 1];
				if (add.OpCode.Code != Code.Add)
					continue;
				var ldarg = instrs[i + 2];
				if (!ldarg.IsLdarg())
					continue;
				var sub = instrs[i + 3];
				if (sub.OpCode.Code != Code.Sub)
					continue;
				var ldci4 = instrs[i + 4];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0xFF)
					continue;

				return ldarg.GetParameterIndex();
			}

			return -1;
		}

		static int GetMagicArgIndex41Trial(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!ldarg.IsLdarg())
					continue;
				if (!instrs[i + 1].IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Shr)
					continue;

				return ldarg.GetParameterIndex();
			}
			return -1;
		}

		static FieldDef GetLdtokenField(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDef;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				return field;
			}
			return null;
		}

		static string[] handlerLocalTypes_V3 = new string[] {
			"System.AppDomain",
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.String,System.String>",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.String",
			"System.String[]",
		};
		static bool CheckHandlerV3(MethodDef handler) => new LocalTypes(handler).All(handlerLocalTypes_V3);

		static Data40 CheckHandlerV40(MethodDef handler) {
			var data40 = new Data40();

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = i;

				if (instrs[index++].OpCode.Code != Code.Ldarg_1)
					continue;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDef;

				string methodSig = "(System.ResolveEventArgs,System.RuntimeFieldHandle,System.Int32,System.String,System.Int32)";
				var method = ldtoken.Operand as MethodDef;
				if (method != null) {
					// >= 4.0.4
					if (!DotNetUtils.IsMethod(method, "System.Byte[]", "()"))
						continue;
					field = GetResourceField(method);
					methodSig = "(System.ResolveEventArgs,System.RuntimeMethodHandle,System.Int32,System.String,System.Int32)";
				}
				else {
					// 4.0.1.18 .. 4.0.3
				}

				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				var ldci4_len = instrs[index++];
				if (!ldci4_len.IsLdcI4())
					continue;
				if (ldci4_len.GetLdcI4Value() != field.InitialValue.Length)
					continue;

				if (instrs[index++].OpCode.Code != Code.Ldstr)
					continue;

				var ldci4_magic = instrs[index++];
				if (!ldci4_magic.IsLdcI4())
					continue;
				data40.magic = ldci4_magic.GetLdcI4Value();

				var call = instrs[index++];
				if (call.OpCode.Code == Code.Tailcall)
					call = instrs[index++];
				if (call.OpCode.Code != Code.Call)
					continue;
				var resolveHandler2 = call.Operand as MethodDef;
				if (!DotNetUtils.IsMethod(resolveHandler2, "System.Reflection.Assembly", methodSig))
					continue;

				data40.resourceField = field;
				data40.getDataMethod = method;
				data40.resolveHandler2 = resolveHandler2;
				return data40;
			}

			return null;
		}

		static FieldDef GetResourceField(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDef;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;
				return field;
			}
			return null;
		}

		public void Initialize() {
			if (resolveHandler == null)
				return;

			if (version == ResourceVersion.V3) {
				simpleDeobfuscator.Deobfuscate(resolveHandler);
				simpleDeobfuscator.DecryptStrings(resolveHandler, deob);
				data30 = new Data30();
				data30.resource = DeobUtils.GetEmbeddedResourceFromCodeStrings(module, resolveHandler);
				if (data30.resource == null) {
					Logger.w("Could not find resource of encrypted resources");
					return;
				}
			}
		}

		public bool MergeResources(out EmbeddedResource rsrc) {
			rsrc = null;

			switch (version) {
			case ResourceVersion.V3:
				if (data30.resource == null)
					return false;

				DeobUtils.DecryptAndAddResources(module, data30.resource.Name.String, () => DecryptResourceV3(data30.resource));
				rsrc = data30.resource;
				return true;

			case ResourceVersion.V40:
				return DecryptResource(data40.resourceField, data40.magic);

			case ResourceVersion.V41:
				return DecryptResource(data41.resourceField, data41.magic);

			default:
				return true;
			}
		}

		bool DecryptResource(FieldDef resourceField, int magic) {
			if (resourceField == null)
				return false;

			string name = $"Embedded data field {resourceField.MDToken.ToInt32():X8} RVA {(uint)resourceField.RVA:X8}";
			DeobUtils.DecryptAndAddResources(module, name, () => DecryptResourceV4(resourceField.InitialValue, magic));
			resourceField.InitialValue = new byte[1];
			resourceField.FieldSig.Type = module.CorLibTypes.Byte;
			resourceField.RVA = 0;
			return true;
		}
	}
}
