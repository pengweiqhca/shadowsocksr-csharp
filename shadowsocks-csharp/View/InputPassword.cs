﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Shadowsocks.Properties;
using Shadowsocks.Controller;

namespace Shadowsocks.View;

public partial class InputPassword : Form
{
    public string password;

    public InputPassword()
    {
        InitializeComponent();
        Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
        Text = I18N.GetString("InputPassword");
        label_info.Text = I18N.GetString(label_info.Text);
    }

    private void buttonOK_Click(object sender, EventArgs e)
    {
        password = textPassword.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void InputPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            password = textPassword.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}