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

			foreach (var tuple in DotNetUtils.getCalledMethods(module, checkMethod)) {
				var method = tuple.Item2;
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

			var resolveHandlerMethod = getLdftnMethod(resolverInitMethod);
			if (resolveHandlerMethod == null)
				return false;

			if (!checkHandlerMethod(resolveHandlerMethod))
				return false;

			initMethod = resolverInitMethod;
			resolveHandler = resolveHandlerMethod;
			return true;
		}

		MethodDefinition getLdftnMethod(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldftn)
					continue;
				var loadedMethod = instr.Operand as MethodDefinition;
				if (loadedMethod != null)
					return loadedMethod;
			}
			return null;
		}

		bool checkHandlerMethod(MethodDefinition handler) {
			if (handler == null || handler.Body == null || !handler.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(handler, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
				return false;
			return checkHandlerMethodInternal(handler);
		}

		protected abstract bool checkHandlerMethodInternal(MethodDefinition handler);

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

			if (BitConverter.ToInt16(data, start) != 0x5A4D)
				return DeobUtils.inflate(data, start, len, true);

			var data2 = new byte[len];
			Array.Copy(data, start, data2, 0, data2.Length);
			return data2;
		}
	}
}
