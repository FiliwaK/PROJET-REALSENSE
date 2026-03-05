namespace DEMOREALSENSE
{
    partial class CameraView
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            cameraPictureBox = new PictureBox();
            distanceLabel = new Label();
            button1 = new Button();
            panel1 = new Panel();
            ((System.ComponentModel.ISupportInitialize)cameraPictureBox).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // cameraPictureBox
            // 
            cameraPictureBox.Dock = DockStyle.Fill;
            cameraPictureBox.Location = new Point(0, 0);
            cameraPictureBox.Name = "cameraPictureBox";
            cameraPictureBox.Size = new Size(1042, 539);
            cameraPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            cameraPictureBox.TabIndex = 0;
            cameraPictureBox.TabStop = false;

            // 
            // distanceLabel
            // 
            distanceLabel.AutoSize = true;
            distanceLabel.Location = new Point(597, 12);
            distanceLabel.Name = "distanceLabel";
            distanceLabel.Size = new Size(120, 20);
            distanceLabel.TabIndex = 1;
            distanceLabel.Text = "Clique sur l'objet";
            // 
            // button1
            // 
            button1.Location = new Point(480, 3);
            button1.Name = "button1";
            button1.Size = new Size(94, 29);
            button1.TabIndex = 2;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // panel1
            // 
            panel1.Controls.Add(button1);
            panel1.Controls.Add(distanceLabel);
            panel1.Dock = DockStyle.Bottom;
            panel1.Location = new Point(0, 479);
            panel1.Name = "panel1";
            panel1.Size = new Size(1042, 60);
            panel1.TabIndex = 3;
            // 
            // CameraView
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1042, 539);
            Controls.Add(panel1);
            Controls.Add(cameraPictureBox);
            Name = "CameraView";
            Text = "CameraView";
            ((System.ComponentModel.ISupportInitialize)cameraPictureBox).EndInit();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox cameraPictureBox;
        private Label distanceLabel;
        private Button button1;
        private Panel panel1;
    }
}