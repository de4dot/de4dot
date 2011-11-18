/*
    Copyright (C) 2011 de4dot@gmail.com

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

using Mono.Cecil;

namespace de4dot.renamer {
	class VariableNameState {
		ExistingNames existingVariableNames = new ExistingNames();
		TypeNames variableNameCreator = new VariableNameCreator();	// For fields and method args

		public virtual VariableNameState clone() {
			var rv = new VariableNameState();
			cloneInit(rv);
			return rv;
		}

		void cloneInit(VariableNameState variableNameState) {
			variableNameState.existingVariableNames = new ExistingNames();
			variableNameState.variableNameCreator = variableNameCreator.clone();
		}

		public void addFieldName(string fieldName) {
			existingVariableNames.add(fieldName);
		}

		public string getNewFieldName(FieldDefinition field) {
			return existingVariableNames.getName(field.Name, () => variableNameCreator.create(field.FieldType));
		}

		public string getNewFieldName(string oldName, INameCreator nameCreator) {
			return existingVariableNames.getName(oldName, () => nameCreator.create());
		}
	}
}
