namespace Ave.Extensions.Documents.Html.Parsing.Tokenizing;

/// <summary>The states of the tokenizer, named and ordered as the HTML Standard has them (§13.2.5.1–§13.2.5.84).</summary>
internal enum TokenizerState
{
    /// <summary>§13.2.5.1.</summary>
    Data,

    /// <summary>§13.2.5.2.</summary>
    Rcdata,

    /// <summary>§13.2.5.3.</summary>
    Rawtext,

    /// <summary>§13.2.5.4.</summary>
    ScriptData,

    /// <summary>§13.2.5.5.</summary>
    Plaintext,

    /// <summary>§13.2.5.6.</summary>
    TagOpen,

    /// <summary>§13.2.5.7.</summary>
    EndTagOpen,

    /// <summary>§13.2.5.8.</summary>
    TagName,

    /// <summary>§13.2.5.9.</summary>
    RcdataLessThanSign,

    /// <summary>§13.2.5.10.</summary>
    RcdataEndTagOpen,

    /// <summary>§13.2.5.11.</summary>
    RcdataEndTagName,

    /// <summary>§13.2.5.12.</summary>
    RawtextLessThanSign,

    /// <summary>§13.2.5.13.</summary>
    RawtextEndTagOpen,

    /// <summary>§13.2.5.14.</summary>
    RawtextEndTagName,

    /// <summary>§13.2.5.15.</summary>
    ScriptDataLessThanSign,

    /// <summary>§13.2.5.16.</summary>
    ScriptDataEndTagOpen,

    /// <summary>§13.2.5.17.</summary>
    ScriptDataEndTagName,

    /// <summary>§13.2.5.18.</summary>
    ScriptDataEscapeStart,

    /// <summary>§13.2.5.19.</summary>
    ScriptDataEscapeStartDash,

    /// <summary>§13.2.5.20.</summary>
    ScriptDataEscaped,

    /// <summary>§13.2.5.21.</summary>
    ScriptDataEscapedDash,

    /// <summary>§13.2.5.22.</summary>
    ScriptDataEscapedDashDash,

    /// <summary>§13.2.5.23.</summary>
    ScriptDataEscapedLessThanSign,

    /// <summary>§13.2.5.24.</summary>
    ScriptDataEscapedEndTagOpen,

    /// <summary>§13.2.5.25.</summary>
    ScriptDataEscapedEndTagName,

    /// <summary>§13.2.5.26.</summary>
    ScriptDataDoubleEscapeStart,

    /// <summary>§13.2.5.27.</summary>
    ScriptDataDoubleEscaped,

    /// <summary>§13.2.5.28.</summary>
    ScriptDataDoubleEscapedDash,

    /// <summary>§13.2.5.29.</summary>
    ScriptDataDoubleEscapedDashDash,

    /// <summary>§13.2.5.30.</summary>
    ScriptDataDoubleEscapedLessThanSign,

    /// <summary>§13.2.5.31.</summary>
    ScriptDataDoubleEscapeEnd,

    /// <summary>§13.2.5.32.</summary>
    BeforeAttributeName,

    /// <summary>§13.2.5.33.</summary>
    AttributeName,

    /// <summary>§13.2.5.34.</summary>
    AfterAttributeName,

    /// <summary>§13.2.5.35.</summary>
    BeforeAttributeValue,

    /// <summary>§13.2.5.36.</summary>
    AttributeValueDoubleQuoted,

    /// <summary>§13.2.5.37.</summary>
    AttributeValueSingleQuoted,

    /// <summary>§13.2.5.38.</summary>
    AttributeValueUnquoted,

    /// <summary>§13.2.5.39.</summary>
    AfterAttributeValueQuoted,

    /// <summary>§13.2.5.40.</summary>
    SelfClosingStartTag,

    /// <summary>§13.2.5.41.</summary>
    BogusComment,

    /// <summary>§13.2.5.42.</summary>
    MarkupDeclarationOpen,

    /// <summary>§13.2.5.43.</summary>
    CommentStart,

    /// <summary>§13.2.5.44.</summary>
    CommentStartDash,

    /// <summary>§13.2.5.45.</summary>
    Comment,

    /// <summary>§13.2.5.46.</summary>
    CommentLessThanSign,

    /// <summary>§13.2.5.47.</summary>
    CommentLessThanSignBang,

    /// <summary>§13.2.5.48.</summary>
    CommentLessThanSignBangDash,

    /// <summary>§13.2.5.49.</summary>
    CommentLessThanSignBangDashDash,

    /// <summary>§13.2.5.50.</summary>
    CommentEndDash,

    /// <summary>§13.2.5.51.</summary>
    CommentEnd,

    /// <summary>§13.2.5.52.</summary>
    CommentEndBang,

    /// <summary>§13.2.5.53.</summary>
    Doctype,

    /// <summary>§13.2.5.54.</summary>
    BeforeDoctypeName,

    /// <summary>§13.2.5.55.</summary>
    DoctypeName,

    /// <summary>§13.2.5.56.</summary>
    AfterDoctypeName,

    /// <summary>§13.2.5.57.</summary>
    AfterDoctypePublicKeyword,

    /// <summary>§13.2.5.58.</summary>
    BeforeDoctypePublicIdentifier,

    /// <summary>§13.2.5.59.</summary>
    DoctypePublicIdentifierDoubleQuoted,

    /// <summary>§13.2.5.60.</summary>
    DoctypePublicIdentifierSingleQuoted,

    /// <summary>§13.2.5.61.</summary>
    AfterDoctypePublicIdentifier,

    /// <summary>§13.2.5.62.</summary>
    BetweenDoctypePublicAndSystemIdentifiers,

    /// <summary>§13.2.5.63.</summary>
    AfterDoctypeSystemKeyword,

    /// <summary>§13.2.5.64.</summary>
    BeforeDoctypeSystemIdentifier,

    /// <summary>§13.2.5.65.</summary>
    DoctypeSystemIdentifierDoubleQuoted,

    /// <summary>§13.2.5.66.</summary>
    DoctypeSystemIdentifierSingleQuoted,

    /// <summary>§13.2.5.67.</summary>
    AfterDoctypeSystemIdentifier,

    /// <summary>§13.2.5.68.</summary>
    BogusDoctype,

    /// <summary>§13.2.5.69.</summary>
    CdataSection,

    /// <summary>§13.2.5.70.</summary>
    CdataSectionBracket,

    /// <summary>§13.2.5.71.</summary>
    CdataSectionEnd,

    /// <summary>§13.2.5.72.</summary>
    ProcessingInstructionOpen,

    /// <summary>§13.2.5.73.</summary>
    ProcessingInstructionTarget,

    /// <summary>§13.2.5.74.</summary>
    AfterProcessingInstructionTarget,

    /// <summary>§13.2.5.75.</summary>
    ProcessingInstructionData,

    /// <summary>§13.2.5.76.</summary>
    ProcessingInstructionQuestionable,

    /// <summary>§13.2.5.77.</summary>
    CharacterReference,

    /// <summary>§13.2.5.78.</summary>
    NamedCharacterReference,

    /// <summary>§13.2.5.79.</summary>
    AmbiguousAmpersand,

    /// <summary>§13.2.5.80.</summary>
    NumericCharacterReference,

    /// <summary>§13.2.5.81.</summary>
    HexadecimalCharacterReferenceStart,

    /// <summary>§13.2.5.82.</summary>
    HexadecimalCharacterReference,

    /// <summary>§13.2.5.83.</summary>
    DecimalCharacterReference,

    /// <summary>§13.2.5.84.</summary>
    NumericCharacterReferenceEnd,
}
