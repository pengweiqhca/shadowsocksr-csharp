﻿using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;

namespace Shadowsocks.View;

public partial class SubscribeForm : Form
{
    private readonly ShadowsocksController controller;
    // this is a copy of configuration that we are working on
    private Configuration _modifiedConfiguration;
    private int _old_select_index;

    public SubscribeForm(ShadowsocksController controller)
    {
        Font = SystemFonts.MessageBoxFont;
        InitializeComponent();

        Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
        this.controller = controller;

        UpdateTexts();
        controller.ConfigChanged += LoadCurrentConfiguration;

        LoadCurrentConfiguration();
    }

    private void UpdateTexts()
    {
        Text = I18N.GetString("Subscribe Settings");
        label1.Text = I18N.GetString("URL");
        label2.Text = I18N.GetString("Group name");
        checkBoxAutoUpdate.Text = I18N.GetString("Auto update");
        buttonOK.Text = I18N.GetString("OK");
        buttonCancel.Text = I18N.GetString("Cancel");
        label3.Text = I18N.GetString("Last Update");
    }

    private void SubscribeForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        controller.ConfigChanged -= LoadCurrentConfiguration;
    }

    private void LoadCurrentConfiguration()
    {
        _modifiedConfiguration = controller.GetConfiguration();
        LoadAllSettings();
        if (listServerSubscribe.Items.Count == 0)
        {
            textBoxURL.Enabled = false;
        }
        else
        {
            textBoxURL.Enabled = true;
        }
    }

    private void LoadAllSettings()
    {
        var select_index = 0;
        checkBoxAutoUpdate.Checked = _modifiedConfiguration.nodeFeedAutoUpdate;
        UpdateList();
        UpdateSelected(select_index);
        SetSelectIndex(select_index);
    }

    private int SaveAllSettings()
    {
        _modifiedConfiguration.nodeFeedAutoUpdate = checkBoxAutoUpdate.Checked;
        return 0;
    }

    private void buttonCancel_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void buttonOK_Click(object sender, EventArgs e)
    {
        var select_index = listServerSubscribe.SelectedIndex;
        SaveSelected(select_index);
        if (SaveAllSettings() == -1)
        {
            return;
        }
        controller.SaveServersConfig(_modifiedConfiguration);
        Close();
    }

    private void UpdateList()
    {
        listServerSubscribe.Items.Clear();
        for (var i = 0; i < _modifiedConfiguration.serverSubscribes.Count; ++i)
        {
            var ss = _modifiedConfiguration.serverSubscribes[i];
            listServerSubscribe.Items.Add((string.IsNullOrEmpty(ss.Group) ? "    " : $"{ss.Group} - ") + ss.URL);
        }
    }

    private void SetSelectIndex(int index)
    {
        if (index >= 0 && index < _modifiedConfiguration.serverSubscribes.Count)
        {
            listServerSubscribe.SelectedIndex = index;
        }
    }

    private void UpdateSelected(int index)
    {
        if (index >= 0 && index < _modifiedConfiguration.serverSubscribes.Count)
        {
            var ss = _modifiedConfiguration.serverSubscribes[index];
            textBoxURL.Text = ss.URL;
            textBoxGroup.Text = ss.Group;
            _old_select_index = index;
            if (ss.LastUpdateTime != 0)
            {
                var now = new DateTime(1970, 1, 1, 0, 0, 0);
                now = now.AddSeconds(ss.LastUpdateTime);
                textUpdate.Text = $"{now.ToLongDateString()} {now.ToLongTimeString()}";
            }
            else
            {
                textUpdate.Text = "(｢･ω･)｢";
            }
        }
    }

    private void SaveSelected(int index)
    {
        if (index >= 0 && index < _modifiedConfiguration.serverSubscribes.Count)
        {
            var ss = _modifiedConfiguration.serverSubscribes[index];
            if (ss.URL != textBoxURL.Text)
            {
                ss.URL = textBoxURL.Text;
                ss.Group = "";
                ss.LastUpdateTime = 0;
            }
        }
    }

    private void listServerSubscribe_SelectedIndexChanged(object sender, EventArgs e)
    {
        var select_index = listServerSubscribe.SelectedIndex;
        if (_old_select_index == select_index)
            return;

        SaveSelected(_old_select_index);
        UpdateList();
        UpdateSelected(select_index);
        SetSelectIndex(select_index);
    }

    private void buttonAdd_Click(object sender, EventArgs e)
    {
        SaveSelected(_old_select_index);
        var select_index = _modifiedConfiguration.serverSubscribes.Count;
        if (_old_select_index >= 0 && _old_select_index < _modifiedConfiguration.serverSubscribes.Count)
        {
            _modifiedConfiguration.serverSubscribes.Insert(select_index, new ServerSubscribe());
        }
        else
        {
            _modifiedConfiguration.serverSubscribes.Add(new ServerSubscribe());
        }
        UpdateList();
        UpdateSelected(select_index);
        SetSelectIndex(select_index);

        textBoxURL.Enabled = true;
    }

    private void buttonDel_Click(object sender, EventArgs e)
    {
        var select_index = listServerSubscribe.SelectedIndex;
        if (select_index >= 0 && select_index < _modifiedConfiguration.serverSubscribes.Count)
        {
            _modifiedConfiguration.serverSubscribes.RemoveAt(select_index);
            if (select_index >= _modifiedConfiguration.serverSubscribes.Count)
            {
                select_index = _modifiedConfiguration.serverSubscribes.Count - 1;
            }
            UpdateList();
            UpdateSelected(select_index);
            SetSelectIndex(select_index);
        }
        if (listServerSubscribe.Items.Count == 0)
        {
            textBoxURL.Enabled = false;
        }
    }

    private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e)
    {

    }
}