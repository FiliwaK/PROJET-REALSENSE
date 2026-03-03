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
            ((System.ComponentModel.ISupportInitialize)cameraPictureBox).BeginInit();
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
            distanceLabel.Location = new Point(454, 434);
            distanceLabel.Name = "distanceLabel";
            distanceLabel.Size = new Size(120, 20);
            distanceLabel.TabIndex = 1;
            distanceLabel.Text = "Clique sur l'objet";
            // 
            // CameraView
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1042, 539);
            Controls.Add(distanceLabel);
            Controls.Add(cameraPictureBox);
            Name = "CameraView";
            Text = "CameraView";
            ((System.ComponentModel.ISupportInitialize)cameraPictureBox).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private PictureBox cameraPictureBox;
        private Label distanceLabel;
    }
}