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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	abstract class ResolverBase {
		protected ModuleDefinition module;
		protected ISimpleDeobfuscator simpleDeobfuscator;
		protected IDeobfuscator deob;
		protected MethodDefinition initMethod;
		protected MethodDefinition resolveHandler;
		protected FrameworkType frameworkType;

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public MethodDefinition HandlerMethod {
			get { return resolveHandler; }
		}

		public bool Detected {
			get { return initMethod != null; }
		}

		public ResolverBase(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.frameworkType = DotNetUtils.getFrameworkType(module);
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public void find() {
			if (checkCalledMethods(DotNetUtils.getModuleTypeCctor(module)))
				return;
			if (checkCalledMethods(module.EntryPoint))
				return;
		}

		bool checkCalledMethods(MethodDefinition checkMethod) {
			if (checkMethod == null || checkMethod.Body == null)
				return false;

			foreach (var method in DotNetUtils.getCalledMethods(module, checkMethod)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;

				if (checkResolverInitMethod(method))
					return true;
			}

			return false;
		}

		bool checkResolverInitMethod(MethodDefinition resolverInitMethod) {
			if (resolverInitMethod == null || resolverInitMethod.Body == null)
				return false;
			if (resolverInitMethod.Body.ExceptionHandlers.Count != 1)
				return false;

			switch (frameworkType) {
			case FrameworkType.Silverlight:
				return checkResolverInitMethodSilverlight(resolverInitMethod);
			default:
				return checkResolverInitMethodDesktop(resolverInitMethod);
			}
		}

		bool checkResolverInitMethodDesktop(MethodDefinition resolverInitMethod) {
			simpleDeobfuscator.deobfuscate(resolverInitMethod);
			if (!checkResolverInitMethodInternal(resolverInitMethod))
				return false;

			foreach (var resolveHandlerMethod in getLdftnMethods(resolverInitMethod)) {
				if (!checkHandlerMethodDesktop(resolveHandlerMethod))
					continue;

				initMethod = resolverInitMethod;
				resolveHandler = resolveHandlerMethod;
				return true;
			}

			return false;
		}

		protected virtual bool checkResolverInitMethodSilverlight(MethodDefinition resolverInitMethod) {
			return false;
		}

		protected abstract bool checkResolverInitMethodInternal(MethodDefinition resolverInitMethod);

		IEnumerable<MethodDefinition> getLdftnMethods(MethodDefinition method) {
			var list = new List<MethodDefinition>();
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldftn)
					continue;
				var loadedMethod = instr.Operand as MethodDefinition;
				if (loadedMethod != null)
					list.Add(loadedMethod);
			}
			return list;
		}

		bool checkHandlerMethodDesktop(MethodDefinition handler) {
			if (handler == null || handler.Body == null || !handler.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(handler, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
				return false;
			return checkHandlerMethodDesktopInternal(handler);
		}

		protected abstract bool checkHandlerMethodDesktopInternal(MethodDefinition handler);

		// 3.0.3.41 - 3.0.4.44
		protected static byte[] decryptResourceV3Old(EmbeddedResource resource) {
			return decryptResourceV3Old(resource.GetResourceData());
		}

		// 3.0.3.41 - 3.0.4.44
		protected static byte[] decryptResourceV3Old(byte[] data) {
			return decryptResource(data, 0, data.Length, 0);
		}

		protected static byte[] decryptResourceV41SL(EmbeddedResource resource) {
			var data = resource.GetResourceData();
			byte k = data[0];
			for (int i = 0; i < data.Length - 1; i++)
				data[i + 1] ^= (byte)((k << (i & 5)) + i);
			return inflateIfNeeded(data, 1, data.Length - 1);
		}

		protected static byte[] decryptResourceV3(EmbeddedResource resource) {
			return decryptResourceV3(resource.GetResourceData());
		}

		protected static byte[] decryptResourceV3(byte[] data) {
			return decryptResource(data, 1, data.Length - 1, data[0]);
		}

		protected static byte[] decryptResourceV4(byte[] data, int magic) {
			return decryptResource(data, 0, data.Length, magic);
		}

		protected static byte[] decryptResource(byte[] data, int start, int len, int magic) {
			for (int i = start; i < start + len; i++)
				data[i] ^= (byte)(i - start + magic);
			return inflateIfNeeded(data, start, len);
		}

		protected static byte[] inflateIfNeeded(byte[] data) {
			return inflateIfNeeded(data, 0, data.Length);
		}

		protected static byte[] inflateIfNeeded(byte[] data, int start, int len) {
			if (BitConverter.ToInt16(data, start) != 0x5A4D)
				return DeobUtils.inflate(data, start, len, true);

			var data2 = new byte[len];
			Array.Copy(data, start, data2, 0, data2.Length);
			return data2;
		}
	}
}
