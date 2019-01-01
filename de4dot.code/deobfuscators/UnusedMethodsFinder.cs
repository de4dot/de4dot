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

namespace de4dot.code.deobfuscators {
	public class UnusedMethodsFinder {
		ModuleDef module;
		MethodCollection removedMethods;
		Dictionary<MethodDef, bool> possiblyUnusedMethods = new Dictionary<MethodDef, bool>();
		Stack<MethodDef> notUnusedStack = new Stack<MethodDef>();

		public UnusedMethodsFinder(ModuleDef module, IEnumerable<MethodDef> possiblyUnusedMethods, MethodCollection removedMethods) {
			this.module = module;
			this.removedMethods = removedMethods;
			foreach (var method in possiblyUnusedMethods) {
				if (method != module.ManagedEntryPoint && !removedMethods.Exists(method))
					this.possiblyUnusedMethods[method] = true;
			}
		}

		public IEnumerable<MethodDef> Find() {
			if (possiblyUnusedMethods.Count == 0)
				return possiblyUnusedMethods.Keys;

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods)
					Check(method);
			}

			while (notUnusedStack.Count > 0) {
				var method = notUnusedStack.Pop();
				if (!possiblyUnusedMethods.Remove(method))
					continue;
				Check(method);
			}

			return possiblyUnusedMethods.Keys;
		}

		void Check(MethodDef method) {
			if (method.Body == null)
				return;
			if (possiblyUnusedMethods.ContainsKey(method))
				return;
			if (removedMethods.Exists(method))
				return;

			foreach (var instr in method.Body.Instructions) {
				switch (instr.OpCode.Code) {
				case Code.Call:
				case Code.Calli:
				case Code.Callvirt:
				case Code.Newobj:
				case Code.Ldtoken:
				case Code.Ldftn:
				case Code.Ldvirtftn:
					break;
				default:
					continue;
				}

				var calledMethod = DotNetUtils.GetMethod2(module, instr.Operand as IMethod);
				if (calledMethod == null)
					continue;
				if (possiblyUnusedMethods.ContainsKey(calledMethod))
					notUnusedStack.Push(calledMethod);
			}
		}
	}
}
