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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	abstract class ResolverBase {
		protected ModuleDefMD module;
		protected ISimpleDeobfuscator simpleDeobfuscator;
		protected IDeobfuscator deob;
		protected MethodDef initMethod;
		protected MethodDef resolveHandler;
		protected FrameworkType frameworkType;

		public MethodDef InitMethod => initMethod;
		public MethodDef HandlerMethod => resolveHandler;
		public bool Detected => initMethod != null;

		public ResolverBase(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			frameworkType = DotNetUtils.GetFrameworkType(module);
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public void Find() {
			if (CheckCalledMethods(DotNetUtils.GetModuleTypeCctor(module)))
				return;
			if (CheckCalledMethods(module.EntryPoint))
				return;
		}

		bool CheckCalledMethods(MethodDef checkMethod) {
			if (checkMethod == null || checkMethod.Body == null)
				return false;

			foreach (var method in DotNetUtils.GetCalledMethods(module, checkMethod)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;

				if (CheckResolverInitMethod(method))
					return true;
			}

			return false;
		}

		bool CheckResolverInitMethod(MethodDef resolverInitMethod) {
			if (resolverInitMethod == null || resolverInitMethod.Body == null)
				return false;
			if (resolverInitMethod.Body.ExceptionHandlers.Count != 1)
				return false;

			switch (frameworkType) {
			case FrameworkType.Silverlight:
				return CheckResolverInitMethodSilverlight(resolverInitMethod);
			default:
				return CheckResolverInitMethodDesktop(resolverInitMethod);
			}
		}

		bool CheckResolverInitMethodDesktop(MethodDef resolverInitMethod) {
			simpleDeobfuscator.Deobfuscate(resolverInitMethod);
			if (!CheckResolverInitMethodInternal(resolverInitMethod))
				return false;

			foreach (var resolveHandlerMethod in GetLdftnMethods(resolverInitMethod)) {
				if (!CheckHandlerMethodDesktop(resolveHandlerMethod))
					continue;

				initMethod = resolverInitMethod;
				resolveHandler = resolveHandlerMethod;
				return true;
			}

			return false;
		}

		protected virtual bool CheckResolverInitMethodSilverlight(MethodDef resolverInitMethod) => false;
		protected abstract bool CheckResolverInitMethodInternal(MethodDef resolverInitMethod);

		IEnumerable<MethodDef> GetLdftnMethods(MethodDef method) {
			var list = new List<MethodDef>();
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldftn)
					continue;
				if (instr.Operand is MethodDef loadedMethod)
					list.Add(loadedMethod);
			}
			return list;
		}

		bool CheckHandlerMethodDesktop(MethodDef handler) {
			if (handler == null || handler.Body == null || !handler.IsStatic)
				return false;
			if (!DotNetUtils.IsMethod(handler, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
				return false;
			return CheckHandlerMethodDesktopInternal(handler);
		}

		protected abstract bool CheckHandlerMethodDesktopInternal(MethodDef handler);

		// 3.0.3.41 - 3.0.4.44
		protected static byte[] DecryptResourceV3Old(EmbeddedResource resource) =>
			DecryptResourceV3Old(resource.CreateReader().ToArray());

		// 3.0.3.41 - 3.0.4.44
		protected static byte[] DecryptResourceV3Old(byte[] data) =>
			DecryptResource(data, 0, data.Length, 0);

		protected static byte[] DecryptResourceV41SL(EmbeddedResource resource) {
			var data = resource.CreateReader().ToArray();
			byte k = data[0];
			for (int i = 0; i < data.Length - 1; i++)
				data[i + 1] ^= (byte)((k << (i & 5)) + i);
			return InflateIfNeeded(data, 1, data.Length - 1);
		}

		protected static byte[] DecryptResourceV3(EmbeddedResource resource) => DecryptResourceV3(resource.CreateReader().ToArray());
		protected static byte[] DecryptResourceV3(byte[] data) => DecryptResource(data, 1, data.Length - 1, data[0]);
		protected static byte[] DecryptResourceV4(byte[] data, int magic) => DecryptResource(data, 0, data.Length, magic);

		protected static byte[] DecryptResource(byte[] data, int start, int len, int magic) {
			for (int i = start; i < start + len; i++)
				data[i] ^= (byte)(i - start + magic);
			return InflateIfNeeded(data, start, len);
		}

		protected static byte[] InflateIfNeeded(byte[] data) => InflateIfNeeded(data, 0, data.Length);

		protected static byte[] InflateIfNeeded(byte[] data, int start, int len) {
			if (BitConverter.ToInt16(data, start) != 0x5A4D)
				return DeobUtils.Inflate(data, start, len, true);

			var data2 = new byte[len];
			Array.Copy(data, start, data2, 0, data2.Length);
			return data2;
		}
	}
}
