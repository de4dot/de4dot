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
using dnlib.DotNet;

namespace de4dot.code.renamer.asmmodules {
	public class MemberRefFinder : MemberFinder {
		public void RemoveTypeDef(TypeDef td) {
			if (!TypeDefs.Remove(td))
				throw new ApplicationException(string.Format("Could not remove TypeDef: {0}", td));
		}

		public void RemoveEventDef(EventDef ed) {
			if (!EventDefs.Remove(ed))
				throw new ApplicationException(string.Format("Could not remove EventDef: {0}", ed));
		}

		public void RemoveFieldDef(FieldDef fd) {
			if (!FieldDefs.Remove(fd))
				throw new ApplicationException(string.Format("Could not remove FieldDef: {0}", fd));
		}

		public void RemoveMethodDef(MethodDef md) {
			if (!MethodDefs.Remove(md))
				throw new ApplicationException(string.Format("Could not remove MethodDef: {0}", md));
		}

		public void RemovePropertyDef(PropertyDef pd) {
			if (!PropertyDefs.Remove(pd))
				throw new ApplicationException(string.Format("Could not remove PropertyDef: {0}", pd));
		}
	}
}
