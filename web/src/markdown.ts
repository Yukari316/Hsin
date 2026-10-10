import MarkdownIt from 'markdown-it'
import taskLists from 'markdown-it-task-lists'
import DOMPurify from 'dompurify'

const parser = new MarkdownIt({ html: true, linkify: true, breaks: true }).use(taskLists, { enabled: false })

export function renderAnnouncement(markdown: string): string {
  return DOMPurify.sanitize(parser.render(markdown))
}
