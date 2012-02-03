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
using Mono.Cecil.Metadata;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	class SpicesMethodCallInliner : MethodCallInliner {
		ModuleDefinition module;
		TypeDefinitionDict<bool> validTypes = new TypeDefinitionDict<bool>();

		public SpicesMethodCallInliner(ModuleDefinition module)
			: base(false) {
			this.module = module;
		}

		public bool checkCanInline(MethodDefinition method) {
			return validTypes.find(method.DeclaringType);
		}

		protected override bool canInline(MethodDefinition method) {
			return checkCanInline(method);
		}

		public void initialize() {
			foreach (var type in module.GetTypes()) {
				if (checkValidType(type))
					validTypes.add(type, true);
			}
		}

		static bool checkValidType(TypeDefinition type) {
			if ((type.Attributes & ~TypeAttributes.BeforeFieldInit) != TypeAttributes.NestedAssembly)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields || type.HasNestedTypes)
				return false;
			if (type.GenericParameters.Count > 0)
				return false;
			if (type.IsValueType || type.IsInterface)
				return false;
			if (type.BaseType == null || type.BaseType.EType != ElementType.Object)
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			if (!checkValidTypeMethods(type))
				return false;

			return true;
		}

		static bool checkValidTypeMethods(TypeDefinition type) {
			bool foundCtor = false;
			int numMethods = 0;

			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					return false;
				if (method.Name == ".ctor") {
					if (method.Parameters.Count != 0)
						return false;
					foundCtor = true;
					continue;
				}
				if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig))
					return false;
				if (method.HasPInvokeInfo || method.PInvokeInfo != null)
					return false;
				if (method.GenericParameters.Count > 0)
					return false;

				numMethods++;
			}

			return numMethods > 0 && foundCtor;
		}

		public bool restoreBody(Blocks blocks) {
			if (validTypes.find(blocks.Method.DeclaringType))
				return false;
			if (blocks.Locals.Count > 0)
				return false;
			if (blocks.MethodBlocks.BaseBlocks.Count != 1)
				return false;
			var block = blocks.MethodBlocks.BaseBlocks[0] as Block;
			if (block == null)
				return false;

			MethodDefinition calledMethod;
			if (!checkRestoreBody(block, out calledMethod))
				return false;
			if (calledMethod == blocks.Method)
				return false;

			DotNetUtils.copyBodyFromTo(calledMethod, blocks.Method);
			blocks.updateBlocks();
			return true;
		}

		bool checkRestoreBody(Block block, out MethodDefinition calledMethod) {
			calledMethod = null;

			var instrs = block.Instructions;
			int index;
			for (index = 0; index < instrs.Count; index++) {
				if (DotNetUtils.getArgIndex(instrs[index].Instruction) != index)
					break;
			}

			var call = instrs[index++];
			if (call.OpCode.Code != Code.Call)
				return false;

			calledMethod = call.Operand as MethodDefinition;
			if (calledMethod == null)
				return false;
			if (!checkCanInline(calledMethod))
				return false;

			if (instrs[index++].OpCode.Code != Code.Ret)
				return false;

			return true;
		}

		public List<MethodDefinition> getInlinedMethods() {
			var list = new List<MethodDefinition>();

			foreach (var type in validTypes.getKeys())
				list.AddRange(type.Methods);

			return list;
		}

		public TypeDefinitionDict<bool> getInlinedTypes(IEnumerable<MethodDefinition> unusedMethods) {
			var unused = new MethodDefinitionAndDeclaringTypeDict<bool>();
			foreach (var method in unusedMethods)
				unused.add(method, true);

			var types = new TypeDefinitionDict<bool>();
			foreach (var type in validTypes.getKeys()) {
				if (checkAllMethodsUnused(unused, type))
					types.add(type, true);
			}
			return types;
		}

		static bool checkAllMethodsUnused(MethodDefinitionAndDeclaringTypeDict<bool> unused, TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!unused.find(method))
					return false;
			}
			return true;
		}
	}
}
