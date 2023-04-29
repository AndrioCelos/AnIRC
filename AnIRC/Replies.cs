namespace AnIRC;

/// <summary>Contains constants representing standard IRC reply numerics.</summary>
public static class Replies {
	// Standard numerics: https://modern.ircdocs.horse/index.html, RFC 2812
	public const string ADMIN                = "ADMIN";
	public const string AWAY                 = "AWAY";
	public const string CONNECT              = "CONNECT";
	public const string ERROR                = "ERROR";
	public const string HELP                 = "HELP";
	public const string INFO                 = "INFO";
	public const string INVITE               = "INVITE";
	public const string JOIN                 = "JOIN";
	public const string KICK                 = "KICK";
	public const string KILL                 = "KILL";
	public const string LIST                 = "LIST";
	public const string MODE                 = "MODE";
	public const string MOTD                 = "MOTD";
	public const string NAMES                = "NAMES";
	public const string NICK                 = "NICK";
	public const string NOTICE               = "NOTICE";
	public const string OPER                 = "OPER";
	public const string PART                 = "PART";
	public const string PASS                 = "PASS";
	public const string PING                 = "PING";
	public const string PONG                 = "PONG";
	public const string PRIVMSG              = "PRIVMSG";
	public const string QUIT                 = "QUIT";
	public const string REHASH               = "REHASH";
	public const string RESTART              = "RESTART";
	public const string SQUIT                = "SQUIT";
	public const string STATS                = "STATS";
	public const string TIME                 = "TIME";
	public const string TOPIC                = "TOPIC";
	public const string USER                 = "USER";
	public const string USERHOST             = "USERHOST";
	public const string VERSION              = "VERSION";
	public const string WALLOPS              = "WALLOPS";
	public const string WHO                  = "WHO";
	public const string WHOIS                = "WHOIS";
	public const string RPL_WELCOME          = "001";
	public const string RPL_YOURHOST         = "002";
	public const string RPL_CREATED          = "003";
	public const string RPL_MYINFO           = "004";
	public const string RPL_ISUPPORT         = "005";
	public const string RPL_BOUNCE           = "010";
	public const string RPL_STATSLINKINFO    = "211";
	public const string RPL_STATSCOMMANDS    = "212";
	public const string RPL_STATSCLINE       = "213";
	public const string RPL_STATSILINE       = "215";
	public const string RPL_STATSKLINE       = "216";
	public const string RPL_ENDOFSTATS       = "219";
	public const string RPL_UMODEIS          = "221";
	public const string RPL_STATSLLINE       = "241";
	public const string RPL_STATSUPTIME      = "242";
	public const string RPL_STATSOLINE       = "243";
	public const string RPL_STATSHLINE       = "244";
	public const string RPL_LUSERCLIENT      = "251";
	public const string RPL_LUSEROP          = "252";
	public const string RPL_LUSERUNKNOWN     = "253";
	public const string RPL_LUSERCHANNELS    = "254";
	public const string RPL_LUSERME          = "255";
	public const string RPL_ADMINME          = "256";
	public const string RPL_ADMINLOC1        = "257";
	public const string RPL_ADMINLOC2        = "258";
	public const string RPL_ADMINEMAIL       = "259";
	public const string RPL_TRYAGAIN         = "263";
	public const string RPL_LOCALUSERS       = "265";
	public const string RPL_GLOBALUSERS      = "266";
	public const string RPL_WHOISCERTFP      = "276";
	public const string RPL_NONE             = "300";
	public const string RPL_AWAY             = "301";
	public const string RPL_USERHOST         = "302";
	public const string RPL_ISON             = "303";
	public const string RPL_UNAWAY           = "305";
	public const string RPL_NOWAWAY          = "306";
	public const string RPL_WHOISREGNICK     = "307";
	public const string RPL_WHOISUSER        = "311";
	public const string RPL_WHOISSERVER      = "312";
	public const string RPL_WHOISOPERATOR    = "313";
	public const string RPL_WHOWASUSER       = "314";
	public const string RPL_ENDOFWHO         = "315";
	public const string RPL_WHOISIDLE        = "317";
	public const string RPL_ENDOFWHOIS       = "318";
	public const string RPL_WHOISCHANNELS    = "319";
	public const string RPL_WHOISSPECIAL     = "320";
	public const string RPL_LISTSTART        = "321";
	public const string RPL_LIST             = "322";
	public const string RPL_LISTEND          = "323";
	public const string RPL_CHANNELMODEIS    = "324";
	public const string RPL_CREATIONTIME     = "329";
	public const string RPL_WHOISACCOUNT     = "330";
	public const string RPL_NOTOPIC          = "331";
	public const string RPL_TOPIC            = "332";
	public const string RPL_TOPICWHOTIME     = "333";
	public const string RPL_WHOISACTUALLY    = "338";
	public const string RPL_INVITING         = "341";
	public const string RPL_INVITELIST       = "346";
	public const string RPL_ENDOFINVITELIST  = "347";
	public const string RPL_EXCEPTLIST       = "348";
	public const string RPL_ENDOFEXCEPTLIST  = "349";
	public const string RPL_VERSION          = "351";
	public const string RPL_WHOREPLY         = "352";
	public const string RPL_NAMREPLY         = "353";
	public const string RPL_LINKS            = "364";
	public const string RPL_ENDOFNAMES       = "366";
	public const string RPL_BANLIST          = "367";
	public const string RPL_ENDOFBANLIST     = "368";
	public const string RPL_ENDOFWHOWAS      = "369";
	public const string RPL_INFO             = "371";
	public const string RPL_MOTD             = "372";
	public const string RPL_ENDOFMOTD        = "376";
	public const string RPL_WHOISHOST        = "378";
	public const string RPL_WHOISMODES       = "379";
	public const string RPL_YOUREOPER        = "381";
	public const string RPL_REHASHING        = "382";
	public const string RPL_TIME             = "391";
	public const string ERR_UNKNOWNERROR     = "400";
	public const string ERR_NOSUCHNICK       = "401";
	public const string ERR_NOSUCHSERVER     = "402";
	public const string ERR_NOSUCHCHANNEL    = "403";
	public const string ERR_CANNOTSENDTOCHAN = "404";
	public const string ERR_TOOMANYCHANNELS  = "405";
	public const string ERR_TOOMANYTARGETS   = "407";
	public const string ERR_NOORIGIN         = "409";
	public const string ERR_NORECIPIENT      = "411";
	public const string ERR_NOTEXTTOSEND     = "412";
	public const string ERR_NOTOPLEVEL       = "413";
	public const string ERR_WILDTOPLEVEL     = "414";
	public const string ERR_UNKNOWNCOMMAND   = "421";
	public const string ERR_NOMOTD           = "422";
	public const string ERR_NONICKNAMEGIVEN  = "431";
	public const string ERR_ERRONEUSNICKNAME = "432";
	public const string ERR_NICKNAMEINUSE    = "433";
	public const string ERR_NICKCOLLISION    = "436";
	public const string ERR_UNAVAILRESOURCE  = "437";
	public const string ERR_USERNOTINCHANNEL = "441";
	public const string ERR_NOTONCHANNEL     = "442";
	public const string ERR_USERONCHANNEL    = "443";
	public const string ERR_NOTREGISTERED    = "451";
	public const string ERR_NEEDMOREPARAMS   = "461";
	public const string ERR_ALREADYREGISTRED = "462";
	public const string ERR_PASSWDMISMATCH   = "464";
	public const string ERR_YOUREBANNEDCREEP = "465";
	public const string ERR_KEYSET           = "467";
	public const string ERR_CHANNELISFULL    = "471";
	public const string ERR_UNKNOWNMODE      = "472";
	public const string ERR_INVITEONLYCHAN   = "473";
	public const string ERR_BANNEDFROMCHAN   = "474";
	public const string ERR_BADCHANNELKEY    = "475";
	public const string ERR_BADCHANMASK      = "476";
	public const string ERR_NOPRIVILEGES     = "481";
	public const string ERR_CHANOPRIVSNEEDED = "482";
	public const string ERR_CANTKILLSERVER   = "483";
	public const string ERR_RESTRICTED       = "484";
	public const string ERR_NOOPERHOST       = "491";
	public const string ERR_UMODEUNKNOWNFLAG = "501";
	public const string ERR_USERSDONTMATCH   = "502";
	public const string ERR_HELPNOTFOUND     = "524";
	public const string ERR_INVALIDKEY       = "525";
	public const string RPL_WHOISSECURE      = "671";
	public const string ERR_INVALIDMODEPARAM = "696";
	public const string RPL_HELPSTART        = "704";
	public const string RPL_HELPTXT          = "705";
	public const string RPL_ENDOFHELP        = "706";
	public const string ERR_NOPRIVS          = "723";

	// Non-standard replies
	public const string PROTOCTL             = "PROTOCTL";  // Various; superceded by IRCv3
	public const string RPL_WHOISHELPOP      = "310";  // InspIRCd, Unreal

	// WHOX extension: no specification known; originally from ircu
	public const string RPL_WHOSPCRPL        = "354";

	// IRCv3 extensions: https://ircv3.net/
	public const string ACCOUNT              = "ACCOUNT";
	public const string CAP                  = "CAP";
	public const string CHGHOST              = "CHGHOST";
	public const string TAGMSG               = "TAGMSG";

	// IRCv3 TLS extension: https://ircv3.net/specs/deprecated/tls
	public const string STARTTLS             = "STARTTLS";
	public const string RPL_STARTTLS         = "670";
	public const string ERR_STARTTLS         = "691";

	// IRCv3 Monitor extension: https://ircv3.net/specs/extensions/monitor
	public const string MONITOR              = "MONITOR";
	public const string RPL_MONONLINE        = "730";
	public const string RPL_MONOFFLINE       = "731";
	public const string RPL_MONLIST          = "732";
	public const string RPL_ENDOFMONLIST     = "733";
	public const string RPL_MONLISTFULL      = "734";

	// IRCv3 SASL extension: https://ircv3.net/specs/extensions/sasl-3.1
	public const string AUTHENTICATE         = "AUTHENTICATE";
	public const string RPL_LOGGEDIN         = "900";
	public const string RPL_LOGGEDOUT        = "901";
	public const string ERR_NICKLOCKED       = "902";
	public const string RPL_SASLSUCCESS      = "903";
	public const string ERR_SASLFAIL         = "904";
	public const string ERR_SASLTOOLONG      = "905";
	public const string ERR_SASLABORTED      = "906";
	public const string ERR_SASLALREADY      = "907";
	public const string RPL_SASLMECHS        = "908";

	// Obsolete WATCH extension: https://github.com/grawity/irc-docs/blob/master/client/draft-meglio-irc-watch-00.txt
	public const string WATCH                = "WATCH";
	/// <summary>Notifies that a user in the WATCH list has gone away.</summary>
	public const string RPL_GONEAWAY         = "598";
	/// <summary>Notifies that a user in the WATCH list has returned from being away.</summary>
	public const string RPL_NOTAWAY          = "599";
	/// <summary>Notifies that a user in the WATCH list has come online.</summary>
	public const string RPL_LOGON            = "600";
	/// <summary>Notifies that a user in the WATCH list has disconnected.</summary>
	public const string RPL_LOGOFF           = "601";
	/// <summary>Confirms that a WATCH entry has been removed.</summary>
	public const string RPL_WATCHOFF         = "602";
	/// <summary>Returns stats from the <c>WATCH S</c> command.</summary>
	public const string RPL_WATCHSTAT        = "603";
	/// <summary>Indicates that a user added to the WATCH list or listed with <c>WATCH L</c> is online.</summary>
	public const string RPL_NOWON            = "604";
	/// <summary>Indicates that a user added to the WATCH list or listed with <c>WATCH L</c> is offline.</summary>
	public const string RPL_NOWOFF           = "605";
	/// <summary>Lists nicknames in the WATCH list in response to <c>WATCH S</c>.</summary>
	public const string RPL_WATCHLIST        = "606";
	/// <summary>Indicates the end of the WATCH list from <c>WATCH L</c> or <c>WATCH S</c>.</summary>
	public const string RPL_ENDOFWATCHLIST   = "607";
	/// <summary>Confirms that the WATCH list has been cleared.</summary>
	public const string RPL_CLEARWATCH       = "608";
	/// <summary>Together with RPL_NOWON, indicates that a user added to the WATCH list or listed with <c>WATCH L</c> is away.</summary>
	public const string RPL_NOWISAWAY        = "609";
}
