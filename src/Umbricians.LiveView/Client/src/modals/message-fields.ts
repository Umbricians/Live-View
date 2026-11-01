import { html } from "@umbraco-cms/backoffice/external/lit";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import type { MessageColor } from "../api/live-view.resource.js";

/** The server turns down anything longer (MessageContent), so the fields stop typing at the same lengths. */
export const MAX_HEADLINE_LENGTH = 200;
export const MAX_MESSAGE_LENGTH = 2000;

const TYPES: Array<{ name: string; value: MessageColor }> = [
  { name: "Information", value: "default" },
  { name: "Warning", value: "warning" },
  { name: "Urgent", value: "danger" },
  { name: "Good news", value: "positive" },
];

/** What every message says, to backoffice users or members alike. */
export interface MessageText {
  headline: string;
  message: string;
  color: MessageColor;
}

export const newMessage = (): MessageText => ({ headline: "", message: "", color: "warning" });

/** Whether there is a headline to send, the one field a message can't do without. */
export const hasHeadline = (text: MessageText) => text.headline.trim().length > 0;

/** The text as it is sent. */
export const trimmed = (text: MessageText): MessageText => ({
  headline: text.headline.trim(),
  message: text.message.trim(),
  color: text.color,
});

/** The send button's colour: red for an urgent message. */
export const sendColor = (text: MessageText) => (text.color === "danger" ? "danger" : "positive");

/**
 * The headline, type and message fields, for a dialog's own form, so pressing Enter in the headline still sends.
 * `onChange` gets the whole text back after each edit.
 */
export function renderMessageFields(
  text: MessageText,
  onChange: (text: MessageText) => void,
  placeholders: { headline: string; message: string },
  rows: number,
) {
  return html`
    <uui-form-layout-item>
      <uui-label slot="label" for="headline" required>Headline</uui-label>
      <uui-input
        id="headline"
        label="Headline"
        placeholder=${placeholders.headline}
        maxlength=${MAX_HEADLINE_LENGTH}
        .value=${text.headline}
        @input=${(e: InputEvent) => onChange({ ...text, headline: (e.target as HTMLInputElement).value })}></uui-input>
    </uui-form-layout-item>

    <uui-form-layout-item>
      <uui-label slot="label" for="type">Type</uui-label>
      <uui-select
        id="type"
        label="Type"
        .options=${TYPES.map((t) => ({ ...t, selected: t.value === text.color }))}
        @change=${(e: UUISelectEvent) => onChange({ ...text, color: e.target.value as MessageColor })}></uui-select>
    </uui-form-layout-item>

    <uui-form-layout-item>
      <uui-label slot="label" for="message">Message</uui-label>
      <uui-textarea
        id="message"
        label="Message"
        placeholder=${placeholders.message}
        maxlength=${MAX_MESSAGE_LENGTH}
        rows=${rows}
        .value=${text.message}
        @input=${(e: InputEvent) => onChange({ ...text, message: (e.target as HTMLTextAreaElement).value })}></uui-textarea>
    </uui-form-layout-item>
  `;
}
