/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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

namespace de4dot.code.resources {
	class UserResourceType {
		readonly string name;
		readonly ResourceTypeCode code;

		public string Name {
			get { return name; }
		}

		public ResourceTypeCode Code {
			get { return code; }
		}

		public UserResourceType(string name, ResourceTypeCode code) {
			this.name = name;
			this.code = code;
		}

		public override string ToString() {
			return string.Format("{0:X2} {1}", (int)code, name);
		}
	}
}
