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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class UnusedMethodsFinder {
		ModuleDefinition module;
		MethodCollection removedMethods;
		Dictionary<MethodDefinition, bool> possiblyUnusedMethods = new Dictionary<MethodDefinition, bool>();
		Stack<MethodDefinition> notUnusedStack = new Stack<MethodDefinition>();

		public UnusedMethodsFinder(ModuleDefinition module, IEnumerable<MethodDefinition> possiblyUnusedMethods, MethodCollection removedMethods) {
			this.module = module;
			this.removedMethods = removedMethods;
			foreach (var method in possiblyUnusedMethods) {
				if (method != module.EntryPoint && !removedMethods.exists(method))
					this.possiblyUnusedMethods[method] = true;
			}
		}

		public IEnumerable<MethodDefinition> find() {
			if (possiblyUnusedMethods.Count == 0)
				return possiblyUnusedMethods.Keys;

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods)
					check(method);
			}

			while (notUnusedStack.Count > 0) {
				var method = notUnusedStack.Pop();
				if (!possiblyUnusedMethods.Remove(method))
					continue;
				check(method);
			}

			return possiblyUnusedMethods.Keys;
		}

		void check(MethodDefinition method) {
			if (method.Body == null)
				return;
			if (possiblyUnusedMethods.ContainsKey(method))
				return;
			if (removedMethods.exists(method))
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

				var calledMethod = DotNetUtils.getMethod2(module, instr.Operand as MethodReference);
				if (calledMethod == null)
					continue;
				if (possiblyUnusedMethods.ContainsKey(calledMethod))
					notUnusedStack.Push(calledMethod);
			}
		}
	}
}
