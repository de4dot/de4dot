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
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class CompositeOpCodeHandler {
		public List<BlockSigInfo> BlockSigInfos { get; private set; }
		public List<HandlerTypeCode> TypeCodes { get; private set; }

		public CompositeOpCodeHandler(List<BlockSigInfo> blockSigInfos) {
			this.BlockSigInfos = blockSigInfos;
			this.TypeCodes = new List<HandlerTypeCode>();
		}

		public override string ToString() {
			return OpCodeHandlerInfo.GetCompositeName(TypeCodes);
		}
	}
}
