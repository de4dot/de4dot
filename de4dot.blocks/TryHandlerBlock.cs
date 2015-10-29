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

using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks {
	// Contains the filter handler block and the catch handler block.
	public class TryHandlerBlock : ScopeBlock {
		FilterHandlerBlock filterHandlerBlock = new FilterHandlerBlock();
		HandlerBlock handlerBlock = new HandlerBlock();

		// State for an ExceptionHandler instance
		ITypeDefOrRef catchType;
		ExceptionHandlerType handlerType;

		public ITypeDefOrRef CatchType {
			get { return catchType; }
		}

		public ExceptionHandlerType HandlerType {
			get { return handlerType; }
		}

		public FilterHandlerBlock FilterHandlerBlock {
			get { return filterHandlerBlock; }
		}

		public HandlerBlock HandlerBlock {
			get { return handlerBlock; }
		}

		public TryHandlerBlock(ExceptionHandler handler) {
			this.catchType = handler.CatchType;
			this.handlerType = handler.HandlerType;
		}
	}
}
