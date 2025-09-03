﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Chats.BE.DB;

[Table("UserMcp")]
[Index("McpServerId", Name = "IX_UserMcp_McpServerId")]
[Index("UserId", Name = "IX_UserMcp_UserId")]
public partial class UserMcp
{
    [Key]
    public int Id { get; set; }

    public int McpServerId { get; set; }

    public string? CustomHeaders { get; set; }

    public int UserId { get; set; }

    [ForeignKey("McpServerId")]
    [InverseProperty("UserMcps")]
    public virtual McpServer McpServer { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserMcps")]
    public virtual User User { get; set; } = null!;
}
