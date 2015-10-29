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

namespace de4dot.blocks {
	// This is the block inside try { }.
	public class TryBlock : ScopeBlock {
		// The first one is the most nested one and the last one is the
		// outer most handler. I.e., the exceptions are written to the
		// image in the same order they're saved here.
		List<TryHandlerBlock> handlerBlocks = new List<TryHandlerBlock>();

		public List<TryHandlerBlock> TryHandlerBlocks {
			get { return handlerBlocks; }
		}

		public void AddTryHandler(TryHandlerBlock tryHandlerBlock) {
			handlerBlocks.Add(tryHandlerBlock);
		}
	}
}
