/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class HandlerMethod {
		public MethodDef Method { get; private set; }
		public Blocks Blocks { get; private set; }

		public HandlerMethod(MethodDef method) {
			this.Method = method;
			this.Blocks = new Blocks(method);
		}
	}

	class PrimitiveHandlerMethod : HandlerMethod {
		public MethodSigInfo Sig { get; set; }

		public PrimitiveHandlerMethod(MethodDef method)
			: base(method) {
		}
	}

	class CompositeOpCodeHandler {
		public TypeDef HandlerType { get; private set; }
		public HandlerMethod ExecMethod { get; private set; }
		public List<OpCodeHandlerInfo> OpCodeHandlerInfos { get; private set; }

		public CompositeOpCodeHandler(TypeDef handlerType, HandlerMethod execMethod) {
			this.HandlerType = handlerType;
			this.ExecMethod = execMethod;
			this.OpCodeHandlerInfos = new List<OpCodeHandlerInfo>();
		}

		public override string ToString() {
			if (OpCodeHandlerInfos.Count == 0)
				return "<nothing>";
			var sb = new StringBuilder();
			foreach (var handler in OpCodeHandlerInfos) {
				if (sb.Length != 0)
					sb.Append(", ");
				sb.Append(handler.Name);
			}
			return sb.ToString();
		}
	}
}
