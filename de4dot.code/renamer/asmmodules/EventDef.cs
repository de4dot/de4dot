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
using dot10.DotNet;

namespace de4dot.code.renamer.asmmodules {
	class MEventDef : Ref {
		public MMethodDef AddMethod { get; set; }
		public MMethodDef RemoveMethod { get; set; }
		public MMethodDef RaiseMethod { get; set; }

		public EventDef EventDef {
			get { return (EventDef)memberReference; }
		}

		public MEventDef(EventDef eventDefinition, MTypeDef owner, int index)
			: base(eventDefinition, owner, index) {
		}

		public IEnumerable<MethodDef> methodDefinitions() {
			if (EventDef.AddMethod != null)
				yield return EventDef.AddMethod;
			if (EventDef.RemoveMethod != null)
				yield return EventDef.RemoveMethod;
			if (EventDef.InvokeMethod != null)
				yield return EventDef.InvokeMethod;
			if (EventDef.OtherMethods != null) {
				foreach (var m in EventDef.OtherMethods)
					yield return m;
			}
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (method.IsVirtual)
					return true;
			}
			return false;
		}
	}
}
