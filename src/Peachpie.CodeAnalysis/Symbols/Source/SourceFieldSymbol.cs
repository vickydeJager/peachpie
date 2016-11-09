﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;

namespace Pchp.CodeAnalysis.Symbols
{
    internal partial class SourceFieldSymbol : FieldSymbol//, IAttributeTargetSymbol
    {
        readonly SourceTypeSymbol _type;
        readonly string _name;
        readonly PhpMemberAttributes _modifiers;
        readonly PHPDocBlock _phpdoc;

        /// <summary>
        /// Optional. The field initializer expression.
        /// </summary>
        public BoundExpression Initializer => _initializer;
        readonly BoundExpression _initializer;

        public SourceFieldSymbol(SourceTypeSymbol type, string name, PhpMemberAttributes modifiers, PHPDocBlock phpdoc, BoundExpression initializer = null)
        {
            Contract.ThrowIfNull(type);
            Contract.ThrowIfNull(name);

            _type = type;
            _name = name;
            _modifiers = modifiers;
            _phpdoc = phpdoc;
            _initializer = initializer;
        }

        public override string Name => _name;

        public override Symbol AssociatedSymbol => null;

        public override Symbol ContainingSymbol => _type;

        internal override PhpCompilation DeclaringCompilation => _type.DeclaringCompilation;

        public override ImmutableArray<CustomModifier> CustomModifiers => ImmutableArray<CustomModifier>.Empty;

        public override Accessibility DeclaredAccessibility => _modifiers.GetAccessibility();

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override bool IsConst => false;

        public override bool IsReadOnly => false;

        public override bool IsStatic => _modifiers.IsStatic();

        public override bool IsVolatile => false;

        public override ImmutableArray<Location> Locations
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override bool HasRuntimeSpecialName => false;

        internal override bool HasSpecialName => false;

        internal override bool IsNotSerialized => false;

        internal override MarshalPseudoCustomAttributeData MarshallingInformation => null;

        internal override ObsoleteAttributeData ObsoleteAttributeData => null;

        internal override int? TypeLayoutOffset => null;

        internal override ConstantValue GetConstantValue(bool earlyDecodingWellKnownAttributes)
        {
            return Initializer.ConstantValue;
        }

        internal override TypeSymbol GetFieldType(ConsList<FieldSymbol> fieldsBeingBound)
        {
            var vartag = _phpdoc?.GetElement<PHPDocBlock.VarTag>();
            if (vartag != null && vartag.TypeNamesArray.Length != 0)
            {
                var typectx = TypeRefFactory.CreateTypeRefContext(_type);
                var tmask = PHPDoc.GetTypeMask(typectx, vartag.TypeNamesArray, NameUtils.GetNamingContext(_type.Syntax));
                var t = DeclaringCompilation.GetTypeFromTypeRef(typectx, tmask);
                return t;
            }

            // TODO: analysed PHP type

            return DeclaringCompilation.CoreTypes.PhpValue;
        }
    }

    internal class SourceConstSymbol : SourceFieldSymbol
    {
        public SourceConstSymbol(SourceTypeSymbol type, string name, PHPDocBlock phpdoc, BoundExpression initializer)
            : base(type, name, PhpMemberAttributes.Public | PhpMemberAttributes.Static, phpdoc, initializer)
        {
        }

        public override bool IsConst => true;

        public override bool IsReadOnly => false;

        internal override TypeSymbol GetFieldType(ConsList<FieldSymbol> fieldsBeingBound)
        {
            var cvalue = GetConstantValue(false);
            if (cvalue.IsNull)
                return this.DeclaringCompilation.GetSpecialType(SpecialType.System_Object);

            return this.DeclaringCompilation.GetSpecialType(cvalue.SpecialType);
        }
    }

    internal class SourceRuntimeConstantSymbol : SourceFieldSymbol
    {
        public SourceRuntimeConstantSymbol(SourceTypeSymbol type, string name, PHPDocBlock phpdoc, BoundExpression initializer = null)
            : base(type, name, PhpMemberAttributes.Public, phpdoc, initializer)
        {

        }

        public override bool IsReadOnly => true;
    }
}