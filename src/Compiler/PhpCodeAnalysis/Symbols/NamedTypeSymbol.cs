﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis;
using Cci = Microsoft.Cci;

namespace Pchp.CodeAnalysis.Symbols
{
    /// <summary>
    /// Represents a type other than an array, a pointer, a type parameter, and dynamic.
    /// </summary>
    internal abstract partial class NamedTypeSymbol : TypeSymbol, INamedTypeSymbol
    {
        public abstract int Arity { get; }

        /// <summary>
        /// Should the name returned by Name property be mangled with [`arity] suffix in order to get metadata name.
        /// Must return False for a type with Arity == 0.
        /// </summary>
        internal abstract bool MangleName
        {
            // Intentionally no default implementation to force consideration of appropriate implementation for each new subclass
            get;
        }

        public override SymbolKind Kind => SymbolKind.NamedType;

        public ISymbol AssociatedSymbol => null;

        INamedTypeSymbol INamedTypeSymbol.ConstructedFrom => ConstructedFrom;

        public virtual NamedTypeSymbol ConstructedFrom => this;

        /// <summary>
        /// Get the both instance and static constructors for this type.
        /// </summary>
        public virtual ImmutableArray<MethodSymbol> Constructors => GetMembers()
            .OfType<MethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Constructor || m.MethodKind == MethodKind.StaticConstructor)
            .ToImmutableArray();

        public virtual ImmutableArray<MethodSymbol> InstanceConstructors => GetMembers()
            .OfType<MethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Constructor)
            .ToImmutableArray();

        public virtual ImmutableArray<MethodSymbol> StaticConstructors =>
            GetMembers()
            .OfType<MethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.StaticConstructor)
            .ToImmutableArray();

        /// <summary>
        /// Gets optional <c>.phpnew</c> method.
        /// </summary>
        internal virtual MethodSymbol PhpNewMethodSymbol => GetMembers(WellKnownPchpNames.PhpNewMethodName).OfType<MethodSymbol>().SingleOrDefault();

        internal abstract ImmutableArray<NamedTypeSymbol> GetDeclaredInterfaces(ConsList<Symbol> basesBeingResolved);

        /// <summary>
        /// Requires less computation than <see cref="TypeSymbol.TypeKind"/> == <see cref="TypeKind.Interface"/>.
        /// </summary>
        /// <remarks>
        /// Metadata types need to compute their base types in order to know their TypeKinds, and that can lead
        /// to cycles if base types are already being computed.
        /// </remarks>
        /// <returns>True if this is an interface type.</returns>
        internal abstract bool IsInterface { get; }

        /// <summary>
        /// For delegate types, gets the delegate's invoke method.  Returns null on
        /// all other kinds of types.  Note that it is possible to have an ill-formed
        /// delegate type imported from metadata which does not have an Invoke method.
        /// Such a type will be classified as a delegate but its DelegateInvokeMethod
        /// would be null.
        /// </summary>
        public MethodSymbol DelegateInvokeMethod
        {
            get
            {
                if (TypeKind != TypeKind.Delegate)
                {
                    return null;
                }

                var methods = GetMembers(WellKnownMemberNames.DelegateInvokeName);
                if (methods.Length != 1)
                {
                    return null;
                }

                var method = methods[0] as MethodSymbol;

                //EDMAURER we used to also check 'method.IsVirtual' because section 13.6
                //of the CLI spec dictates that it be virtual, but real world
                //working metadata has been found that contains an Invoke method that is
                //marked as virtual but not newslot (both of those must be combined to
                //meet the C# definition of virtual). Rather than weaken the check
                //I've removed it, as the Dev10 compiler makes no check, and we don't
                //stand to gain anything by having it.

                //return method != null && method.IsVirtual ? method : null;
                return method;
            }
        }

        INamedTypeSymbol INamedTypeSymbol.EnumUnderlyingType => EnumUnderlyingType;

        /// <summary>
        /// For enum types, gets the underlying type. Returns null on all other
        /// kinds of types.
        /// </summary>
        public virtual NamedTypeSymbol EnumUnderlyingType => null;

        public virtual bool IsGenericType => false;

        public virtual bool IsImplicitClass => false;

        public virtual bool IsScriptClass => false;

        public virtual bool IsUnboundGenericType => false;

        public virtual IEnumerable<string> MemberNames
        {
            get
            {
                yield break;
            }
        }

        /// <summary>
        /// True if the type is a Windows runtime type.
        /// </summary>
        /// <remarks>
        /// A type can me marked as a Windows runtime type in source by applying the WindowsRuntimeImportAttribute.
        /// WindowsRuntimeImportAttribute is a pseudo custom attribute defined as an internal class in System.Runtime.InteropServices.WindowsRuntime namespace.
        /// This is needed to mark Windows runtime types which are redefined in mscorlib.dll and System.Runtime.WindowsRuntime.dll.
        /// These two assemblies are special as they implement the CLR's support for WinRT.
        /// </remarks>
        internal abstract bool IsWindowsRuntimeImport { get; }

        /// <summary>
        /// True if the type should have its WinRT interfaces projected onto .NET types and
        /// have missing .NET interface members added to the type.
        /// </summary>
        internal abstract bool ShouldAddWinRTMembers { get; }

        /// <summary>
        /// Returns a flag indicating whether this symbol has at least one applied/inherited conditional attribute.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// </remarks>
        internal bool IsConditional
        {
            get
            {
                //if (this.GetAppliedConditionalSymbols().Any())    // TODO
                //{
                //    return true;
                //}

                // Conditional attributes are inherited by derived types.
                var baseType = this.BaseType;// NoUseSiteDiagnostics;
                return (object)baseType != null ? baseType.IsConditional : false;
            }
        }
        
        /// <summary>
        /// Type layout information (ClassLayout metadata and layout kind flags).
        /// </summary>
        internal abstract TypeLayout Layout { get; }

        public virtual bool MightContainExtensionMethods
        {
            get
            {
                return false;
            }
        }

        internal NamedTypeSymbol ConstructWithoutModifiers(ImmutableArray<TypeSymbol> arguments, bool unbound)
        {
            ImmutableArray<TypeWithModifiers> modifiedArguments;

            if (arguments.IsDefault)
            {
                modifiedArguments = default(ImmutableArray<TypeWithModifiers>);
            }
            else if (arguments.IsEmpty)
            {
                modifiedArguments = ImmutableArray<TypeWithModifiers>.Empty;
            }
            else
            {
                var builder = ArrayBuilder<TypeWithModifiers>.GetInstance(arguments.Length);
                foreach (TypeSymbol t in arguments)
                {
                    builder.Add((object)t == null ? default(TypeWithModifiers) : new TypeWithModifiers(t));
                }

                modifiedArguments = builder.ToImmutableAndFree();
            }

            return Construct(modifiedArguments, unbound);
        }

        internal NamedTypeSymbol Construct(ImmutableArray<TypeWithModifiers> arguments, bool unbound)
        {
            if (!ReferenceEquals(this, ConstructedFrom) || this.Arity == 0)
            {
                throw new InvalidOperationException();
            }

            if (arguments.IsDefault)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            //if (arguments.Any(TypeSymbolIsNullFunction))
            //{
            //    throw new ArgumentException(CSharpResources.TypeArgumentCannotBeNull, "typeArguments");
            //}

            if (arguments.Length != this.Arity)
            {
                throw new ArgumentException();// (CSharpResources.WrongNumberOfTypeArguments, "typeArguments");
            }

            //Debug.Assert(!unbound || arguments.All(TypeSymbolIsErrorType));

            if (ConstructedNamedTypeSymbol.TypeParametersMatchTypeArguments(this.TypeParameters, arguments))
            {
                return this;
            }

            return this.ConstructCore(arguments, unbound);
        }

        protected virtual NamedTypeSymbol ConstructCore(ImmutableArray<TypeWithModifiers> typeArguments, bool unbound)
        {
            return new ConstructedNamedTypeSymbol(this, typeArguments, unbound);
        }

        internal NamedTypeSymbol GetUnboundGenericTypeOrSelf()
        {
            if (!this.IsGenericType)
            {
                return this;
            }

            return this.ConstructUnboundGenericType();
        }

        ImmutableArray<ITypeSymbol> INamedTypeSymbol.TypeArguments => StaticCast<ITypeSymbol>.From(TypeArguments);

        public virtual ImmutableArray<TypeSymbol> TypeArguments => ImmutableArray<TypeSymbol>.Empty;

        public virtual ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        /// <summary>
        /// The original definition of this symbol. If this symbol is constructed from another
        /// symbol by type substitution then OriginalDefinition gets the original symbol as it was defined in
        /// source or metadata.
        /// </summary>
        public new virtual NamedTypeSymbol OriginalDefinition => this;

        protected override TypeSymbol OriginalTypeSymbolDefinition => OriginalDefinition;

        INamedTypeSymbol INamedTypeSymbol.OriginalDefinition => this.OriginalDefinition;

        /// <summary>
        /// Returns the map from type parameters to type arguments.
        /// If this is not a generic type instantiation, returns null.
        /// The map targets the original definition of the type.
        /// </summary>
        internal virtual TypeMap TypeSubstitution
        {
            get { return null; }
        }

        internal virtual NamedTypeSymbol AsMember(NamedTypeSymbol newOwner)
        {
            Debug.Assert(this.IsDefinition);
            Debug.Assert(ReferenceEquals(newOwner.OriginalDefinition, this.ContainingSymbol.OriginalDefinition));
            return newOwner.IsDefinition ? this : new SubstitutedNestedTypeSymbol((SubstitutedNamedTypeSymbol)newOwner, this);
        }

        /// <summary>
        /// PHP constructor method in this class.
        /// Can be <c>null</c>.
        /// </summary>
        internal MethodSymbol ResolvePhpCtor(bool recursive = false)
        {
            var ctor = 
                this.GetMembers(Syntax.Name.SpecialMethodNames.Construct.Value).OfType<MethodSymbol>().FirstOrDefault() ??
                this.GetMembers(this.Name).OfType<MethodSymbol>().FirstOrDefault();

            if (ctor == null && recursive)
            {
                ctor = this.BaseType?.ResolvePhpCtor(true);
            }

            return ctor;
        }

        #region INamedTypeSymbol

        /// <summary>
        /// Get the both instance and static constructors for this type.
        /// </summary>
        ImmutableArray<IMethodSymbol> INamedTypeSymbol.Constructors => StaticCast<IMethodSymbol>.From(Constructors);

        ImmutableArray<IMethodSymbol> INamedTypeSymbol.InstanceConstructors
            => StaticCast<IMethodSymbol>.From(InstanceConstructors);

        ImmutableArray<IMethodSymbol> INamedTypeSymbol.StaticConstructors
            => StaticCast<IMethodSymbol>.From(StaticConstructors);

        ImmutableArray<ITypeParameterSymbol> INamedTypeSymbol.TypeParameters => StaticCast<ITypeParameterSymbol>.From(this.TypeParameters);

        INamedTypeSymbol INamedTypeSymbol.ConstructUnboundGenericType() => ConstructUnboundGenericType();

        INamedTypeSymbol INamedTypeSymbol.Construct(params ITypeSymbol[] arguments)
        {
            //foreach (var arg in arguments)
            //{
            //    arg.EnsureCSharpSymbolOrNull<ITypeSymbol, TypeSymbol>("typeArguments");
            //}
            Debug.Assert(arguments.All(t => t is TypeSymbol));

            return this.Construct(arguments.Cast<TypeSymbol>().ToArray());
        }

        IMethodSymbol INamedTypeSymbol.DelegateInvokeMethod => DelegateInvokeMethod;

        #endregion

        #region ISymbol Members

        public override void Accept(SymbolVisitor visitor)
        {
            visitor.VisitNamedType(this);
        }

        public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
        {
            return visitor.VisitNamedType(this);
        }

        /// <summary>
        /// Returns a constructed type given its type arguments.
        /// </summary>
        /// <param name="typeArguments">The immediate type arguments to be replaced for type
        /// parameters in the type.</param>
        public NamedTypeSymbol Construct(params TypeSymbol[] typeArguments)
        {
            return ConstructWithoutModifiers(typeArguments.AsImmutableOrNull(), false);
        }

        /// <summary>
        /// Returns a constructed type given its type arguments.
        /// </summary>
        /// <param name="typeArguments">The immediate type arguments to be replaced for type
        /// parameters in the type.</param>
        public NamedTypeSymbol Construct(ImmutableArray<TypeSymbol> typeArguments)
        {
            return ConstructWithoutModifiers(typeArguments, false);
        }

        /// <summary>
        /// Returns a constructed type given its type arguments.
        /// </summary>
        /// <param name="typeArguments"></param>
        public NamedTypeSymbol Construct(IEnumerable<TypeSymbol> typeArguments)
        {
            return ConstructWithoutModifiers(typeArguments.AsImmutableOrNull(), false);
        }

        /// <summary>
        /// Returns an unbound generic type of this named type.
        /// </summary>
        public NamedTypeSymbol ConstructUnboundGenericType()
        {
            return OriginalDefinition.AsUnboundGenericType();
        }

        #endregion
    }
}
