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

namespace de4dot.code.deobfuscators.ILProtector {
	abstract class MethodsDecrypterBase {
		protected ModuleDefMD module;
		protected MainType mainType;
		protected EmbeddedResource methodsResource;
		protected Dictionary<int, DecryptedMethodInfo> methodInfos = new Dictionary<int, DecryptedMethodInfo>();
		List<TypeDef> delegateTypes = new List<TypeDef>();

		public EmbeddedResource Resource {
			get { return methodsResource; }
		}

		public IEnumerable<TypeDef> DelegateTypes {
			get { return delegateTypes; }
		}

		public bool MethodReaderHasDelegateTypeFlag { get; set; }

		public MethodsDecrypterBase(ModuleDefMD module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public void Decrypt() {
			DecryptInternal();
			RestoreMethods();
		}

		protected abstract void DecryptInternal();

		void RestoreMethods() {
			if (methodInfos.Count == 0)
				return;

			Logger.v("Restoring {0} methods", methodInfos.Count);
			Logger.Instance.Indent();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method.Body == null)
						continue;

					if (RestoreMethod(method)) {
						Logger.v("Restored method {0} ({1:X8}). Instrs:{2}, Locals:{3}, Exceptions:{4}",
							Utils.RemoveNewlines(method.FullName),
							method.MDToken.ToInt32(),
							method.Body.Instructions.Count,
							method.Body.Variables.Count,
							method.Body.ExceptionHandlers.Count);
					}
				}
			}
			Logger.Instance.DeIndent();
			if (methodInfos.Count != 0)
				Logger.w("{0} methods weren't restored", methodInfos.Count);
		}

		bool RestoreMethod(MethodDef method) {
			int? methodId = GetMethodId(method);
			if (methodId == null)
				return false;

			var parameters = method.Parameters;
			var methodInfo = methodInfos[methodId.Value];
			methodInfos.Remove(methodId.Value);
			var methodReader = new MethodReader(module, methodInfo.data, parameters);
			methodReader.HasDelegateTypeFlag = MethodReaderHasDelegateTypeFlag;
			methodReader.Read(method);

			RestoreMethod(method, methodReader);
			if (methodReader.DelegateType != null)
				delegateTypes.Add(methodReader.DelegateType);

			return true;
		}

		static void RestoreMethod(MethodDef method, MethodReader methodReader) {
			// body.MaxStackSize = <let dnlib calculate this>
			method.Body.InitLocals = methodReader.InitLocals;
			methodReader.RestoreMethod(method);
		}

		protected int? GetMethodId(MethodDef method) {
			if (method == null || method.Body == null)
				return null;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldsfld = instrs[i];
				if (ldsfld.OpCode.Code != Code.Ldsfld)
					continue;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;

				var field = ldsfld.Operand as FieldDef;
				if (field == null || field != mainType.InvokerInstanceField)
					continue;

				return ldci4.GetLdcI4Value();
			}

			return null;
		}
	}
}
